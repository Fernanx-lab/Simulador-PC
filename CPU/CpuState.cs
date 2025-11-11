namespace ProjetoSimuladorPC.Cpu
{
    /// <summary>
    /// Estado interno da CPU com nomes em português e endereços em int
    /// (compatível com a implementação de RamState que usa índices int).
    /// </summary>
    public class CpuState
    {
        public int ContadorPrograma { get; set; }
        public uint Acumulador { get; set; }
        public bool InterrupcaoHabilitada { get; set; } = true;
        public bool Parado { get; set; } = false;
        public int UltimoEnderecoAcesso { get; set; }
        public uint UltimoDadoLido { get; set; }
        public uint UltimoDadoEscrito { get; set; }
        public string OperacaoAtual { get; set; } = "NOP";

        public void Reset()
        {
            ContadorPrograma = 0;
            Acumulador = 0;
            InterrupcaoHabilitada = true;
            Parado = false;
            UltimoEnderecoAcesso = 0;
            UltimoDadoLido = 0;
            UltimoDadoEscrito = 0;
            OperacaoAtual = "RESET";
        }
    }
}