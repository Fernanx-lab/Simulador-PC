
namespace ProjetoSimIO.Core
{
    /// <summary>
    /// Implementação simples de PIC: fila FIFO de vetores, com máscara por linha.
    /// Não é um PIC completo (prioridade fixa FIFO), mas serve para simulação básica.
    /// </summary>
    public class PicController : IPicController
    {
        private readonly Queue<int> fila = new();
        private readonly HashSet<int> pendentes = new();
        private readonly HashSet<int> mascaradas = new();

        public bool HasPendingIrq()
        {
            lock (fila)
            {
                return fila.Count > 0;
            }
        }

        public int GetPendingVector()
        {
            lock (fila)
            {
                return fila.Count > 0 ? fila.Peek() : -1;
            }
        }

        public void AckIrq(int vector)
        {
            lock (fila)
            {
                if (fila.Count == 0) return;
                int atual = fila.Peek();
                if (atual == vector)
                {
                    fila.Dequeue();
                    pendentes.Remove(vector);
                }
                else
                {
                    // Se vetor não for o do topo, tente removê-lo da lista de pendentes
                    if (pendentes.Contains(vector))
                    {
                        // reconstruir fila sem o vetor (simples)
                        var items = fila.ToArray().Where(v => v != vector).ToArray();
                        fila.Clear();
                        foreach (var v in items) fila.Enqueue(v);
                        pendentes.Remove(vector);
                    }
                }
            }
        }

        public void RaiseIrq(int irqLine)
        {
            lock (fila)
            {
                if (mascaradas.Contains(irqLine)) return;
                if (pendentes.Contains(irqLine)) return;
                fila.Enqueue(irqLine);
                pendentes.Add(irqLine);
            }
        }

        public void MaskIrq(int irqLine)
        {
            lock (fila)
            {
                mascaradas.Add(irqLine);
            }
        }

        public void UnmaskIrq(int irqLine)
        {
            lock (fila)
            {
                mascaradas.Remove(irqLine);
            }
        }
    }
}