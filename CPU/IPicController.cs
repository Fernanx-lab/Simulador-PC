namespace ProjetoSimIO.Core
{
    /// <summary>
    /// Interface mínima para um controlador de interrupções (PIC).
    /// </summary>
    public interface IPicController
    {
        bool HasPendingIrq();
        int GetPendingVector(); // retorna -1 se não houver
        void AckIrq(int vector);

        // Métodos úteis adicionais
        void RaiseIrq(int irqLine);
        void MaskIrq(int irqLine);
        void UnmaskIrq(int irqLine);
    }
}