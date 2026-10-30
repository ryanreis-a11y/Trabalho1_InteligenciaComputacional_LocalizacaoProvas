namespace LocalizacaoProvas;

internal static class HeuristicaReferencia
{
    private const double Epsilon = 1e-9;

    // Tenta ordens gulosas distintas; falha da heurística não prova inviabilidade.
    public static (Solucao Solucao, int Tentativa) Construir(Instancia instancia, int maxTipos, int seed, int tentativas, Historico? historico = null, bool aleatorizar = false)
    {
        if (maxTipos is not (1 or 2) || tentativas < 1) throw new ArgumentException("Parâmetros inválidos da construção.");
        if (instancia.Escolas.Sum(e => (long)e.Capacidade) < instancia.Candidatos.Length)
            throw new ArgumentException("Instância inviável: capacidade total insuficiente.");
        if ((long)instancia.Candidatos.Select(c => c.Tipo).Distinct().Count() > (long)maxTipos * instancia.Escolas.Length)
            throw new ArgumentException("Instância inviável: tipos distintos excedem as vagas de tipos nas escolas.");
        var random = new Random(seed);
        for (int tentativa = 1; tentativa <= tentativas; tentativa++)
        {
            // Alterna estratégias gerais: prioridade individual e concentração por tipo.
            if (tentativa % 2 == 0)
            {
                historico?.Iniciar(tentativa, "Concentração por tipo");
                var agrupada = ConstruirPorTipo(instancia, maxTipos, random, tentativa == 2 && !aleatorizar, historico);
                historico?.Encerrar(agrupada is not null);
                if (agrupada is not null)
                {
                    agrupada.Validar(instancia, maxTipos);
                    return (agrupada, tentativa);
                }
                continue;
            }
            historico?.Iniciar(tentativa, "Arrependimento por candidato");
            var solucao = new Solucao(instancia);
            bool completa = true;
            for (int passo = 0; passo < instancia.Candidatos.Length; passo++)
            {
                var opcoes = Enumerable.Range(0, instancia.Candidatos.Length)
                    .Where(i => solucao.Alocacao[i] < 0)
                    .Select(i =>
                    {
                        var escolas = Enumerable.Range(0, instancia.Escolas.Length)
                            .Where(j => solucao.PodeInserir(instancia, i, j, maxTipos))
                            .OrderBy(j => instancia.Distancias[i][j]).ThenBy(j => j).ToArray();
                        double arrependimento = escolas.Length < 2 ? double.PositiveInfinity
                            : instancia.Distancias[i][escolas[1]] - instancia.Distancias[i][escolas[0]];
                        return (Candidato: i, Escolas: escolas, Arrependimento: arrependimento, Desempate: random.NextDouble());
                    }).ToArray();
                if (opcoes.Any(o => o.Escolas.Length == 0)) { completa = false; break; }
                // Primeira tentativa determinística; reinícios usam lista restrita dos três prioritários.
                var ordenadas = opcoes.OrderBy(o => o.Escolas.Length == 1 ? 0 : 1)
                    .ThenByDescending(o => o.Arrependimento)
                    .ThenBy(o => tentativa == 1 && !aleatorizar ? o.Candidato : o.Desempate).ToArray();
                var escolhido = ordenadas[tentativa == 1 && !aleatorizar ? 0 : random.Next(Math.Min(3, ordenadas.Length))];
                int escola = escolhido.Escolas[0];
                // Nos reinícios, variar também a escola permite escapar de bloqueios por tipos.
                if ((tentativa > 1 || aleatorizar) && random.NextDouble() < 0.25)
                    escola = escolhido.Escolas[random.Next(Math.Min(3, escolhido.Escolas.Length))];
                solucao.Inserir(instancia, escolhido.Candidato, escola);
                historico?.Alocar(instancia, escolhido.Candidato, escola);
            }
            historico?.Encerrar(completa);
            if (completa)
            {
                solucao.Validar(instancia, maxTipos);
                return (solucao, tentativa);
            }
        }
        throw new InvalidOperationException($"Não foi encontrada solução viável em {tentativas} tentativas. Isso não prova inviabilidade; aumente --tentativas ou altere --seed.");
    }

    // Concentra tipos em escolas e aloca os candidatos mais próximos em cada lote.
    // Evita a fragmentação excessiva dos tipos sem depender de tamanhos ou nomes de instâncias.
    private static Solucao? ConstruirPorTipo(Instancia instancia, int maxTipos, Random random, bool deterministica, Historico? historico)
    {
        var solucao = new Solucao(instancia);
        var grupos = Enumerable.Range(0, instancia.Candidatos.Length).GroupBy(i => instancia.Candidatos[i].Tipo)
            .Select(g => (Candidatos: g.ToArray(), Desempate: random.NextDouble()))
            .OrderByDescending(g => deterministica ? g.Candidatos.Length : g.Candidatos.Length * (0.5 + g.Desempate))
            .ToArray();
        foreach (var grupo in grupos)
        {
            var restantes = grupo.Candidatos.ToHashSet();
            while (restantes.Count > 0)
            {
                var opcoes = Enumerable.Range(0, instancia.Escolas.Length)
                    .Where(j => solucao.PodeInserir(instancia, restantes.First(), j, maxTipos))
                    .Select(j =>
                    {
                        int vagas = instancia.Escolas[j].Capacidade - solucao.Ocupacao[j];
                        int[] lote = restantes.OrderBy(i => instancia.Distancias[i][j]).ThenBy(i => i)
                            .Take(vagas).ToArray();
                        return (Escola: j, Lote: lote, Media: lote.Average(i => instancia.Distancias[i][j]));
                    })
                    // Prioriza atender o maior lote possível para poupar vagas de tipos.
                    .OrderByDescending(o => o.Lote.Length).ThenBy(o => o.Media).ThenBy(o => o.Escola).ToArray();
                if (opcoes.Length == 0) return null;
                int indice = deterministica ? 0 : random.Next(Math.Min(3, opcoes.Length));
                var escolha = opcoes[indice];
                foreach (int i in escolha.Lote)
                {
                    solucao.Inserir(instancia, i, escolha.Escola);
                    historico?.Alocar(instancia, i, escolha.Escola);
                    restantes.Remove(i);
                }
            }
        }
        return solucao;
    }

    // VND: após cada melhoria, volta à realocação; encerra sem melhorias nas duas vizinhanças.
    public static (int Realocacoes, int Trocas) Melhorar(Instancia instancia, Solucao solucao, int maxTipos, bool usarTrocas = true, Historico? historico = null)
    {
        int realocacoes = 0, trocas = 0;
        while (true)
        {
            if (Realocar(instancia, solucao, maxTipos, historico)) { realocacoes++; continue; }
            if (usarTrocas && Trocar(instancia, solucao, maxTipos, historico)) { trocas++; continue; }
            solucao.Validar(instancia, maxTipos);
            return (realocacoes, trocas);
        }
    }

    // Aplica a primeira transferência viável cuja distância diminui estritamente.
    private static bool Realocar(Instancia instancia, Solucao solucao, int maxTipos, Historico? historico)
    {
        for (int i = 0; i < instancia.Candidatos.Length; i++)
        {
            int origem = solucao.Alocacao[i];
            for (int destino = 0; destino < instancia.Escolas.Length; destino++)
                if (destino != origem && instancia.Distancias[i][destino] - instancia.Distancias[i][origem] < -Epsilon
                    && solucao.PodeInserir(instancia, i, destino, maxTipos))
                {
                    solucao.Remover(instancia, i);
                    solucao.Inserir(instancia, i, destino);
                    historico?.Melhorar(instancia, i, -1, origem, destino,
                        instancia.Distancias[i][destino] - instancia.Distancias[i][origem]);
                    return true;
                }
        }
        return false;
    }

    // Avalia a troca após as duas remoções, restaurando exatamente o estado se for inviável.
    private static bool Trocar(Instancia instancia, Solucao solucao, int maxTipos, Historico? historico)
    {
        for (int i = 0; i < instancia.Candidatos.Length; i++)
            for (int k = i + 1; k < instancia.Candidatos.Length; k++)
            {
                int a = solucao.Alocacao[i], b = solucao.Alocacao[k];
                if (a == b) continue;
                double delta = instancia.Distancias[i][b] + instancia.Distancias[k][a]
                    - instancia.Distancias[i][a] - instancia.Distancias[k][b];
                if (delta >= -Epsilon) continue;
                solucao.Remover(instancia, i);
                solucao.Remover(instancia, k);
                bool viavel = solucao.PodeInserir(instancia, i, b, maxTipos) && solucao.PodeInserir(instancia, k, a, maxTipos);
                solucao.Inserir(instancia, i, viavel ? b : a);
                solucao.Inserir(instancia, k, viavel ? a : b);
                if (viavel)
                {
                    historico?.Melhorar(instancia, i, k, a, b, delta);
                    return true;
                }
            }
        return false;
    }
}
