using System.Numerics;

namespace LocalizacaoProvas;

public static class BuscaLocalEficiente
{
    // Mantém a mesma primeira melhora da referência, guardando em bits as trocas já avaliadas.
    public static (int Realocacoes, int Trocas) Melhorar(Instancia instancia, Solucao solucao, int maxTipos,
        bool usarTrocas, Historico? historico)
    {
        int n = instancia.Candidatos.Length, realocacoes = 0, trocas = 0;
        var mapaTipos = instancia.Candidatos.Select(c => c.Tipo).Distinct().Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        int[] tipos = instancia.Candidatos.Select(c => mapaTipos[c.Tipo]).ToArray();
        var contagens = instancia.Escolas.Select(_ => new int[mapaTipos.Count]).ToArray();
        var distintos = new int[instancia.Escolas.Length];
        for (int i = 0; i < n; i++) contagens[solucao.Alocacao[i]][tipos[i]]++;
        for (int j = 0; j < distintos.Length; j++) distintos[j] = contagens[j].Count(c => c > 0);
        var bits = usarTrocas ? Enumerable.Range(0, n).Select(_ => new ulong[(n + 63) / 64]).ToArray() : [];
        var primeiraTroca = Enumerable.Repeat(-1, n).ToArray();
        var realocacoesViaveis = Enumerable.Range(0, n).Select(_ => new ulong[(instancia.Escolas.Length + 63) / 64]).ToArray();
        var primeiroDestino = Enumerable.Repeat(-1, n).ToArray();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < instancia.Escolas.Length; j++) AtualizarRealocacao(i, j);
        if (usarTrocas)
            for (int i = 0; i < n; i++)
                for (int k = i + 1; k < n; k++) Atualizar(i, k);
        while (true)
        {
            int candidato = -1, destino = -1, outro = -1;
            for (int i = 0; i < n; i++)
                if (primeiroDestino[i] >= 0) { candidato = i; destino = primeiroDestino[i]; break; }
            if (candidato < 0 && usarTrocas)
                for (int i = 0; i < n; i++)
                    if (primeiraTroca[i] >= 0)
                    { candidato = i; outro = primeiraTroca[i]; destino = solucao.Alocacao[outro]; break; }
            if (candidato < 0) break;
            int origem = solucao.Alocacao[candidato];
            int[] antesA = (int[])contagens[origem].Clone(), antesB = (int[])contagens[destino].Clone();
            bool cheiaA = solucao.Ocupacao[origem] == instancia.Escolas[origem].Capacidade;
            bool cheiaB = solucao.Ocupacao[destino] == instancia.Escolas[destino].Capacidade;
            double delta = instancia.Distancias[candidato][destino] - instancia.Distancias[candidato][origem];
            solucao.Remover(instancia, candidato);
            contagens[origem][tipos[candidato]]--;
            if (outro >= 0)
            {
                delta += instancia.Distancias[outro][origem] - instancia.Distancias[outro][destino];
                solucao.Remover(instancia, outro);
                contagens[destino][tipos[outro]]--;
                solucao.Inserir(instancia, outro, origem);
                contagens[origem][tipos[outro]]++;
                trocas++;
            }
            else realocacoes++;
            solucao.Inserir(instancia, candidato, destino);
            contagens[destino][tipos[candidato]]++;
            distintos[origem] = contagens[origem].Count(c => c > 0);
            distintos[destino] = contagens[destino].Count(c => c > 0);
            historico?.Melhorar(instancia, candidato, outro, origem, destino, delta);
            bool mudouConjuntoA = antesA.Where((c, t) => (c > 0) != (contagens[origem][t] > 0)).Any();
            bool mudouConjuntoB = antesB.Where((c, t) => (c > 0) != (contagens[destino][t] > 0)).Any();
            bool mudouRealA = mudouConjuntoA || cheiaA != (solucao.Ocupacao[origem] == instancia.Escolas[origem].Capacidade);
            bool mudouRealB = mudouConjuntoB || cheiaB != (solucao.Ocupacao[destino] == instancia.Escolas[destino].Capacidade);
            for (int i = 0; i < n; i++)
            {
                if (i == candidato || i == outro)
                    for (int j = 0; j < instancia.Escolas.Length; j++) AtualizarRealocacao(i, j);
                else
                {
                    if (mudouRealA) AtualizarRealocacao(i, origem);
                    if (mudouRealB) AtualizarRealocacao(i, destino);
                }
            }
            if (!usarTrocas) continue;
            for (int i = 0; i < n; i++)
                if (i == candidato || i == outro || (solucao.Alocacao[i] == origem && (mudouConjuntoA || (antesA[tipos[i]] == 1) != (contagens[origem][tipos[i]] == 1)))
                    || (solucao.Alocacao[i] == destino && (mudouConjuntoB || (antesB[tipos[i]] == 1) != (contagens[destino][tipos[i]] == 1))))
                    for (int k = 0; k < n; k++)
                        if (k != i) Atualizar(Math.Min(i, k), Math.Max(i, k));
        }
        solucao.Validar(instancia, maxTipos);
        return (realocacoes, trocas);

        // Reavalia apenas destinos alterados, conservando a primeira realocação em ordem lexicográfica.
        void AtualizarRealocacao(int i, int j)
        {
            bool viavel = j != solucao.Alocacao[i]
                && instancia.Distancias[i][j] - instancia.Distancias[i][solucao.Alocacao[i]] < -1e-9
                && solucao.Ocupacao[j] < instancia.Escolas[j].Capacidade
                && (contagens[j][tipos[i]] > 0 || distintos[j] < maxTipos);
            ulong mascara = 1UL << (j % 64);
            if (viavel)
            {
                realocacoesViaveis[i][j / 64] |= mascara;
                if (primeiroDestino[i] < 0 || j < primeiroDestino[i]) primeiroDestino[i] = j;
            }
            else
            {
                realocacoesViaveis[i][j / 64] &= ~mascara;
                if (primeiroDestino[i] == j) primeiroDestino[i] = PrimeiroBit(realocacoesViaveis[i]);
            }
        }

        // Localiza o menor índice ativo usando operações de bits.
        static int PrimeiroBit(ulong[] palavras)
        {
            for (int p = 0; p < palavras.Length; p++)
                if (palavras[p] != 0) return p * 64 + BitOperations.TrailingZeroCount(palavras[p]);
            return -1;
        }

        // Atualiza um par sem remover candidatos nem modificar os contadores da solução.
        void Atualizar(int i, int k)
        {
            int a = solucao.Alocacao[i], b = solucao.Alocacao[k];
            bool melhora = a != b && instancia.Distancias[i][b] + instancia.Distancias[k][a]
                - instancia.Distancias[i][a] - instancia.Distancias[k][b] < -1e-9;
            if (melhora)
            {
                int ti = tipos[i], tk = tipos[k];
                if (ti != tk)
                    melhora = (contagens[a][tk] > 0 || distintos[a] - (contagens[a][ti] == 1 ? 1 : 0) < maxTipos)
                        && (contagens[b][ti] > 0 || distintos[b] - (contagens[b][tk] == 1 ? 1 : 0) < maxTipos);
            }
            ulong mascara = 1UL << (k % 64);
            if (melhora)
            {
                bits[i][k / 64] |= mascara;
                if (primeiraTroca[i] < 0 || k < primeiraTroca[i]) primeiraTroca[i] = k;
            }
            else
            {
                bits[i][k / 64] &= ~mascara;
                if (primeiraTroca[i] == k) primeiraTroca[i] = PrimeiroBit(bits[i]);
            }
        }
    }
}
