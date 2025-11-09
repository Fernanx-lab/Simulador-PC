/*
 * timer_pic.c
 * Simulador simples do Timer (MMIO) e do PIC (interrupção) para o Projeto 2
 * CMP1057 - Arquitetura de Computadores I
 *
 * Objetivo: fornecer uma implementação modular em C com as funções exigidas
 * pelo contrato do subsistema de E/S:
 *   - read8/16/32(addr) -> data
 *   - write8/16/32(addr, data)
 *   - tick() -> avança 1 ciclo
 *   - irq_pending() -> retorna menor vetor pendente ou -1
 *   - ack_irq(vector) -> reconhecimento/EOI
 *
 * Este arquivo contém:
 *  - implementa��o do Timer (MMIO) com CTRL, PERIOD, COUNT, STATUS
 *  - implementa��o do PIC (MASK, PENDING, EOI, PRIORITY)
 *  - medidor de métricas: latência média de IRQ, jitter, contadores
 *  - exemplo de mapa de endereços e como integrar
 *
 * Compilar: gcc -std=c11 -O2 -o timer_pic_example timer_pic.c
 * (o arquivo também tem um mini-harness de teste no final)
 */

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <inttypes.h>
#include <math.h>

/* ----------------------------- CONFIGURAÇÃO ---------------------------- */

/* Endereços base sugeridos (podem ser remapeados pelo integrador) */
#define MMIO_RAM_END      0x0FFFFFFF
#define TIMER_BASE        0x10000000u
#define TIMER_SIZE        0x100u
#define PIC_BASE          0x10000F00u
#define PIC_SIZE          0x100u

/* Timer offsets (word-addressable) */
#define TIMER_OFF_CTRL    0x00
#define TIMER_OFF_PERIOD  0x04
#define TIMER_OFF_COUNT   0x08
#define TIMER_OFF_STATUS  0x0C

/* PIC offsets */
#define PIC_OFF_MASK      0x00  /* 32-bit mask: bit set -> masked (disabled) */
#define PIC_OFF_PENDING   0x04  /* 32-bit pending bits */
#define PIC_OFF_EOI       0x08  /* write vector to EOI */
#define PIC_OFF_PRIORITY  0x0C  /* optional priorities region (not fully mmapped) */

/* Vectors used */
#define VECTOR_TIMER      32

/* Global cycle counter (simulador ciclo-a-ciclo) */
static uint64_t global_cycles = 0;

/* ------------------------------- ESTRUTURAS -------------------------------- */

typedef struct {
    uint8_t enabled;        /* Timer enable (CTRL bit 0) */
    uint8_t irq_enable;     /* Timer IRQ enable (CTRL bit 1) */
    uint32_t period;        /* Period in cycles */
    uint32_t count;         /* Current counter */
    uint8_t status;         /* STATUS bits (e.g., pending flag) */
    /* metrics */
    uint64_t events_generated;   /* total ticks that generated IRQ */
} TimerDevice;

typedef struct {
    uint32_t mask_bits;     /* 1 == masked (disabled) */
    uint32_t pending_bits;  /* 1 == pending */
    /* simple priority: lower vector number = higher priority
       We'll allow up to 32 vectors (0..31) mapped to IRQ numbers starting at 0
       For our uses we will only use VECTOR_TIMER (32) as example. */
    /* metrics */
    /* latency tracking arrays (per-vector) */
    uint64_t last_event_cycle[64];   /* when device signalled IRQ (to compute latency) */
    uint64_t samples_count[64];
    long double samples_mean[64];    /* mean latency (cycles) */
    long double samples_m2[64];      /* for variance (Welford) */
} PICDevice;

/* Instâncias */
static TimerDevice timer;
static PICDevice pic;

/* ------------------------------- HELPERS ---------------------------------- */

static inline int in_range(uint32_t addr, uint32_t base, uint32_t size) {
    return (addr >= base) && (addr < base + size);
}

/* sinaliza uma interrupção no PIC (set pending bit e registra ciclo do evento) */
static void pic_signal_irq(unsigned vector) {
    if (vector >= 64) return; /* segurança */
    pic.pending_bits |= (1u << (vector % 32));
    pic.last_event_cycle[vector] = global_cycles;
}

/* coloca valor no PIC e atualiza métricas (Welford) quando ack for chamado */
static void pic_record_latency(unsigned vector, uint64_t latency) {
    if (vector >= 64) return;
    pic.samples_count[vector]++;
    long double delta = (long double)latency - pic.samples_mean[vector];
    pic.samples_mean[vector] += delta / pic.samples_count[vector];
    long double delta2 = (long double)latency - pic.samples_mean[vector];
    pic.samples_m2[vector] += delta * delta2;
}

/* ------------------------------- TIMER API -------------------------------- */

static void timer_reset(void) {
    memset(&timer, 0, sizeof(timer));
    timer.period = 0;
    timer.count = 0;
    timer.enabled = 0;
    timer.irq_enable = 0;
}

/* Chamado a cada ciclo pelo simulador */
static void timer_tick(void) {
    if (!timer.enabled) return;
    if (timer.period == 0) return; /* defensivo */

    if (timer.count == 0) {
        /* gerou evento */
        timer.events_generated++;
        timer.status = 1; /* pending */
        /* sinaliza PIC se IRQ enabled */
        if (timer.irq_enable) {
            pic_signal_irq(VECTOR_TIMER % 32);
        }
        /* reload */
        timer.count = timer.period;
    } else {
        timer.count--;
    }
}

/* ------------------------------- PIC API ---------------------------------- */

static void pic_reset(void) {
    memset(&pic, 0, sizeof(pic));
}

/* retorna vetor do primeiro IRQ pendente não mascarado, ou -1 */
static int32_t pic_get_pending_vector(void) {
    uint32_t pending = pic.pending_bits & ~pic.mask_bits;
    if (pending == 0) return -1;
    /* prioridade simples: menor bit set -> menor vetor */
    unsigned bit = __builtin_ctz(pending); /* primeiro bit */
    unsigned vector = bit; /* mapping 1:1 for simplicity */
    return (int32_t)vector;
}

/* EOI / ack: limpar pending; medir latencia do evento */
static void pic_ack(unsigned vector) {
    if (vector >= 32) return;
    /* calcular latencia: ciclos desde evento */
    uint64_t event_cycle = pic.last_event_cycle[vector];
    uint64_t latency = 0;
    if (event_cycle != 0) {
        latency = global_cycles - event_cycle;
        pic_record_latency(vector, latency);
    }
    /* limpar pending */
    pic.pending_bits &= ~(1u << vector);
}

/* ------------------------------- MMIO I/O -------------------------------- */

/* A API abaixo expõe funções read/write que o núcleo usará para acessar MMIO.
   Para simplicidade implementamos apenas 32-bit accesses (read32/write32).
   Integração: se o núcleo usa 8/16/32, basta adaptar (shift/mask). */

uint32_t mmio_read32(uint32_t addr) {
    /* TIMER */
    if (in_range(addr, TIMER_BASE, TIMER_SIZE)) {
        uint32_t off = addr - TIMER_BASE;
        switch (off) {
            case TIMER_OFF_CTRL: {
                uint32_t v = (timer.enabled & 1u) | ((timer.irq_enable & 1u) << 1);
                return v;
            }
            case TIMER_OFF_PERIOD:
                return timer.period;
            case TIMER_OFF_COUNT:
                return timer.count;
            case TIMER_OFF_STATUS:
                return (uint32_t)timer.status;
            default:
                return 0;
        }
    }
    /* PIC */
    if (in_range(addr, PIC_BASE, PIC_SIZE)) {
        uint32_t off = addr - PIC_BASE;
        switch (off) {
            case PIC_OFF_MASK: return pic.mask_bits;
            case PIC_OFF_PENDING: return pic.pending_bits;
            case PIC_OFF_EOI: return 0; /* read not useful */
            default: return 0;
        }
    }
    return 0; /* unmapped reads -> 0 */
}

void mmio_write32(uint32_t addr, uint32_t val) {
    if (in_range(addr, TIMER_BASE, TIMER_SIZE)) {
        uint32_t off = addr - TIMER_BASE;
        switch (off) {
            case TIMER_OFF_CTRL:
                timer.enabled = val & 1u;
                timer.irq_enable = (val >> 1) & 1u;
                break;
            case TIMER_OFF_PERIOD:
                timer.period = val;
                break;
            case TIMER_OFF_COUNT:
                timer.count = val;
                break;
            case TIMER_OFF_STATUS:
                timer.status = val & 0xFF;
                break;
            default:
                break;
        }
        return;
    }
    if (in_range(addr, PIC_BASE, PIC_SIZE)) {
        uint32_t off = addr - PIC_BASE;
        switch (off) {
            case PIC_OFF_MASK:
                pic.mask_bits = val;
                break;
            case PIC_OFF_PENDING:
                /* writing 1s clears pendings (typical behaviour) */
                pic.pending_bits &= ~val;
                break;
            case PIC_OFF_EOI:
                /* val is vector to ack */
                pic_ack(val & 0xFFu);
                break;
            default:
                break;
        }
        return;
    }
    /* unmapped write -> ignored */
}

/* ------------------------------- CONTRATO --------------------------------- */

/* tick(): avança 1 ciclo para todos os dispositivos do subsistema */
void iosub_tick(void) {
    global_cycles++;
    timer_tick();
    /* dispositivos adicionais (console, dma) teriam seus tick() aqui */
}

/* irq_pending(): retorna menor vetor pendente nao mascarado ou -1 */
int32_t iosub_irq_pending(void) {
    return pic_get_pending_vector();
}

/* ack_irq(vector): chamado pelo núcleo quando a ISR começa; retorna true se aceitou */
int iosub_ack_irq(int vector) {
    if (vector < 0) return 0;
    /* ack no PIC (EOI) */
    pic_ack((unsigned)vector);
    return 1;
}

/* read/write de 8/16/32 (adaptadores simples que chamam mmio_read32) */
uint8_t iosub_read8(uint32_t addr) {
    uint32_t base = addr & ~3u;
    uint32_t word = mmio_read32(base);
    unsigned shift = (addr & 3u) * 8u;
    return (uint8_t)((word >> shift) & 0xFFu);
}
uint16_t iosub_read16(uint32_t addr) {
    uint32_t base = addr & ~3u;
    uint32_t word = mmio_read32(base);
    unsigned shift = (addr & 2u) * 8u;
    return (uint16_t)((word >> shift) & 0xFFFFu);
}
uint32_t iosub_read32(uint32_t addr) { return mmio_read32(addr); }

void iosub_write8(uint32_t addr, uint8_t val) {
    uint32_t base = addr & ~3u;
    uint32_t word = mmio_read32(base);
    unsigned shift = (addr & 3u) * 8u;
    uint32_t mask = 0xFFu << shift;
    uint32_t neww = (word & ~mask) | ((uint32_t)val << shift);
    mmio_write32(base, neww);
}

void iosub_write16(uint32_t addr, uint16_t val) {
    uint32_t base = addr & ~3u;
    uint32_t word = mmio_read32(base);
    unsigned shift = (addr & 2u) * 8u;
    uint32_t mask = 0xFFFFu << shift;
    uint32_t neww = (word & ~mask) | ((uint32_t)val << shift);
    mmio_write32(base, neww);
}

void iosub_write32(uint32_t addr, uint32_t val) { mmio_write32(addr, val); }

/* ------------------------------- METRICS ---------------------------------- */

/* retorna estatisticas simples: mean, variance, count para um vetor */
void iosub_get_irq_stats(unsigned vector, uint64_t *count, long double *mean, long double *variance) {
    if (vector >= 64) { *count = 0; *mean = 0; *variance = 0; return; }
    *count = pic.samples_count[vector];
    *mean = pic.samples_mean[vector];
    if (pic.samples_count[vector] > 1)
        *variance = pic.samples_m2[vector] / (pic.samples_count[vector] - 1);
    else
        *variance = 0;
}

uint64_t iosub_get_global_cycles(void) { return global_cycles; }

/* helper para printar um resumo */
void iosub_print_summary(void) {
    printf("[IOSUB] cycles=%" PRIu64 "\n", global_cycles);
    printf("[TIMER] enabled=%u irq_enable=%u period=%u count=%u events=%" PRIu64 "\n",
           timer.enabled, timer.irq_enable, timer.period, timer.count, timer.events_generated);
    uint64_t c; long double m,v;
    iosub_get_irq_stats(VECTOR_TIMER, &c, &m, &v);
    printf("[PIC] timer_vector=%u samples=%" PRIu64 " mean_latency=%.2Lf var=%.2Lf\n",
           VECTOR_TIMER, c, m, v);
}

/* ------------------------------- INICIALIZAÇÃO ------------------------- */

void iosub_init_default(void) {
    global_cycles = 0;
    timer_reset();
    pic_reset();
    /* estado inicial: timer disabled, period 0; integrador vai configurar via MMIO */
}

/* ------------------------------- MINI-HARNESS ------------------------------ */
#ifdef BUILD_TEST_HARNESS
int main(void) {
    iosub_init_default();
    /* configurar timer via MMIO */
    iosub_write32(TIMER_BASE + TIMER_OFF_PERIOD, 10); /* tick a cada 10 ciclos */
    iosub_write32(TIMER_BASE + TIMER_OFF_CTRL, 0x3); /* enabled + irq_enable */

    /* simular 1000 ciclos e o núcleo respondendo a IRQs */
    for (int i = 0; i < 1000; ++i) {
        iosub_tick();
        int32_t v = iosub_irq_pending();
        if (v >= 0) {
            /* simular que CPU entrou na ISR imediatamente (zero delay) */
            iosub_ack_irq(v);
        }
    }
    iosub_print_summary();
    return 0;
}
#endif

/* Fim do arquivo */
