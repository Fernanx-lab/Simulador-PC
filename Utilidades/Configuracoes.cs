namespace ProjetoSimuladorPC.Utilidades
{
    public class Configuracoes
    {
        public int ClockHz { get; set; }
        public CacheConfig Cache { get; set; } = new CacheConfig();
        public BusConfig Bus { get; set; } = new BusConfig();
        public DevicesConfig Devices { get; set; } = new DevicesConfig();
    }
}
