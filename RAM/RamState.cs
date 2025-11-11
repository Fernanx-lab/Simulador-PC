namespace ProjetoSimuladorPC.RAM
{
    public class RamState
    {
        private Ram memoriaInterna => new(80000);

        public ulong Tamanho { get; }

        private int CustoLeitura;

        private int CustoEscrita;

        public long CiclosTotais { get; private set; }

    }
}
