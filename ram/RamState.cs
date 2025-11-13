using System;
using ProjetoSimuladorPC.Cache;

namespace ProjetoSimuladorPC.RAM
{
    /// <summary>
    /// Fachada de acesso à RAM: expõe operações seguras (thread-safe) de leitura/escrita,
    /// properties de tamanho e evento para notificar alterações de memória.
    /// Projetado para ser injetado como serviço em Blazor (ex: singleton).
    /// </summary>
    public class RamState
    {
        private readonly Ram _ram;
        private readonly object _sync = new();

        // cache opcional (pode ser anexada em tempo de execução)
        private ProjetoSimuladorPC.Cache.Cache? _cache;

        public event EventHandler<MemoryChangedEventArgs>? MemoryChanged;

        public int TamanhoEmBytes => _ram.TamanhoEmBytes;
        public int TamanhoEmMB => TamanhoEmBytes / (1024 * 1024);

        public RamState(int tamanhoEmMB) : this(new Ram(tamanhoEmMB)) { }

        public RamState(Ram ram)
        {
            _ram = ram ?? throw new ArgumentNullException(nameof(ram));
        }

        /// <summary>
        /// Anexa uma instância de cache para que leituras/escritas atualizem estatísticas.
        /// </summary>
        public void AttachCache(ProjetoSimuladorPC.Cache.Cache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Lê um byte no endereço especificado.
        /// </summary>
        public byte Ler(int endereco)
        {
            lock (_sync)
            {
                // registra acesso na cache (apenas estatísticas aqui)
                try { _cache?.Access((uint)endereco, false); } catch { }

                return _ram.Ler(endereco);
            }
        }

        /// <summary>
        /// Lê um bloco de bytes a partir do endereço informado.
        /// </summary>
        public byte[] Ler(int endereco, int comprimento)
        {
            lock (_sync)
            {
                // registra um acesso de bloco como um único acesso (ajuste se desejar granularidade)
                try { _cache?.Access((uint)endereco, false); } catch { }

                return _ram.Ler(endereco, comprimento);
            }
        }

        /// <summary>
        /// Tenta ler um bloco; retorna falso se houver erro de limites.
        /// </summary>
        public bool TryLer(int endereco, int comprimento, out byte[] dados)
        {
            try
            {
                dados = Ler(endereco, comprimento);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                dados = Array.Empty<byte>();
                return false;
            }
        }

        /// <summary>
        /// Escreve um único byte e notifica assinantes.
        /// </summary>
        public void Escrever(int endereco, byte valor)
        {
            lock (_sync)
            {
                // registra escrita na cache
                try { _cache?.Access((uint)endereco, true); } catch { }

                _ram.Escrever(endereco, valor);
                OnMemoryChanged(endereco, new[] { valor });
            }
        }

        /// <summary>
        /// Escreve um bloco de bytes e notifica assinantes.
        /// </summary>
        public void Escrever(int endereco, byte[] dados)
        {
            if (dados is null) throw new ArgumentNullException(nameof(dados));

            lock (_sync)
            {
                // registra escrita de bloco como um único acesso (ajuste se desejar granularidade)
                try { _cache?.Access((uint)endereco, true); } catch { }

                _ram.Escrever(endereco, dados);
                OnMemoryChanged(endereco, dados);
            }
        }

        protected virtual void OnMemoryChanged(int endereco, byte[] dados)
        {
            // cria cópia para garantir imutabilidade externa
            var copia = new byte[dados.Length];
            Array.Copy(dados, copia, dados.Length);
            MemoryChanged?.Invoke(this, new MemoryChangedEventArgs(endereco, copia));
        }

        /// <summary>
        /// Retorna uma cópia de todo o conteúdo da RAM (snapshot).
        /// </summary>
        public byte[] Snapshot()
        {
            lock (_sync)
            {
                return Ler(0, TamanhoEmBytes);
            }
        }
    }

    public class MemoryChangedEventArgs : EventArgs
    {
        public int Endereco { get; }
        public byte[] Dados { get; }

        public MemoryChangedEventArgs(int endereco, byte[] dados)
        {
            Endereco = endereco;
            Dados = dados ?? throw new ArgumentNullException(nameof(dados));
        }
    }
}
