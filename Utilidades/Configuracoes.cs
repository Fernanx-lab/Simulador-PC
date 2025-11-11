using System;
using System.ComponentModel.DataAnnotations;

namespace ProjetoSimuladorPC.Utilidades
{
    /// <summary>
    /// Modelo de configurações usado por Counter.razor e por SimulationState.Config.
    /// Propriedades possuem valores padrão compatíveis com o formulário.
    /// </summary>
    public class Configuracoes
    {
        // Geral
        [Required]
        [Range(1, int.MaxValue)]
        public int ClockHz { get; set; } = 100_000_000;

        // Cache L1
        [Required]
        public string L1Type { get; set; } = "unified"; // "unified" | "split"

        [Required]
        public string L1Size { get; set; } = "16KB"; // formato livre (ex: "16KB", "32KB")

        [Required]
        [Range(1, 1024)]
        public int L1Assoc { get; set; } = 2;

        [Required]
        [Range(1, 4096)]
        public int L1LineSize { get; set; } = 64;

        [Required]
        [Range(0, int.MaxValue)]
        public int L1HitCycles { get; set; } = 1;

        [Required]
        [Range(0, int.MaxValue)]
        public int L1MissCycles { get; set; } = 20;

        [Required]
        public string L1WritePolicy { get; set; } = "WT"; // "WT" | "WB"

        public bool L1WriteAlloc { get; set; } = true;

        // Barramento (Bus)
        [Required]
        [Range(1, 16)]
        public int BusWidthBytes { get; set; } = 4;

        [Required]
        [Range(0, 100)]
        public int BusWaitStates { get; set; } = 1;

        [Required]
        public string BusArbitration { get; set; } = "fixed"; // "fixed" | "round_robin"

        // Dispositivos (endereços base em hexadecimal)
        // Mantidos como uint para representar endereços; valores padrão conforme o form.
        [Required]
        public uint TimerBase { get; set; } = 0x1000_0000;

        [Required]
        public uint ConsoleBase { get; set; } = 0x1000_0100;

        [Required]
        public uint DmaBase { get; set; } = 0x1000_0200;

        [Required]
        public uint PicBase { get; set; } = 0x1000_0F00;

        // Dispositivo extras / parâmetros
        [Range(1, 65536)]
        public int TimerPeriodCycles { get; set; } = 5000;

        [Range(1, 1024)]
        public int DmaBurstLen { get; set; } = 16;

        // Utilitários

        /// <summary>
        /// Tenta clonar as configurações (útil para edição temporária no UI).
        /// </summary>
        public Configuracoes Clone()
        {
            return (Configuracoes)MemberwiseClone();
        }

        /// <summary>
        /// Valida o objeto usando DataAnnotations; lança ValidationException em falha.
        /// </summary>
        public void Validate()
        {
            var ctx = new ValidationContext(this);
            Validator.ValidateObject(this, ctx, validateAllProperties: true);
        }
    }
}