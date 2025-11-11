using System;

namespace ProjetoSimuladorPC.RAM
{
    public class Ram
    {
        private readonly byte[] memoria;

        public int TamanhoEmBytes => memoria.Length;

        public Ram(int tamanhoEmMB)
        {
            if (tamanhoEmMB <= 0)
                throw new ArgumentOutOfRangeException(nameof(tamanhoEmMB), "O tamanho deve ser maior que zero.");

            long tamanhoEmBytesLong = (long)tamanhoEmMB * 1024 * 1024;
            if (tamanhoEmBytesLong > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(tamanhoEmMB), "Tamanho muito grande para este endereço.");

            memoria = new byte[(int)tamanhoEmBytesLong];
        }

        // Lê um único byte do endereço especificado, com checagem de limites.
        public byte Ler(int endereco)
        {
            if (endereco < 0 || endereco >= memoria.Length)
                throw new ArgumentOutOfRangeException(nameof(endereco), $"Endereço inválido: {endereco}. Faixa permitida: 0..{memoria.Length - 1}");

            return memoria[endereco];
        }

        // Lê um bloco de bytes a partir do endereço especificado.
        public byte[] Ler(int endereco, int comprimento)
        {
            if (comprimento < 0)
                throw new ArgumentOutOfRangeException(nameof(comprimento), "Comprimento não pode ser negativo.");

            if (endereco < 0 || endereco + comprimento > memoria.Length)
                throw new ArgumentOutOfRangeException(nameof(endereco), $"Leitura fora dos limites: endereço={endereco}, comprimento={comprimento}.");

            var buffer = new byte[comprimento];
            Array.Copy(memoria, endereco, buffer, 0, comprimento);
            return buffer;
        }

        // Métodos auxiliares de escrita para facilitar testes e uso.
        public void Escrever(int endereco, byte valor)
        {
            if (endereco < 0 || endereco >= memoria.Length)
                throw new ArgumentOutOfRangeException(nameof(endereco));

            memoria[endereco] = valor;
        }

        public void Escrever(int endereco, byte[] dados)
        {
            if (dados is null) throw new ArgumentNullException(nameof(dados));
            if (endereco < 0 || endereco + dados.Length > memoria.Length)
                throw new ArgumentOutOfRangeException(nameof(endereco));

            Array.Copy(dados, 0, memoria, endereco, dados.Length);
        }
    }
}
