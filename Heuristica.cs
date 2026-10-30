namespace LocalizacaoProvas;

public static class Heuristica
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
        // A ordem geográfica é fixa; recalculam-se apenas as opções ainda viáveis.
        var ordemEscolas = Enumerable.Range(0, instancia.Candidatos.Length)
            .Select(i => Enumerable.Range(0, instancia.Escolas.Length)
                .OrderBy(j => instancia.Distancias[i][j]).ThenBy(j => j).ToArray()).ToArray();
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
            // Na construção, a viabilidade só diminui na escola que recebeu a última entrada.
            var viaveis = Enumerable.Range(0, instancia.Candidatos.Length)
                .Select(_ => Enumerable.Repeat(true, instancia.Escolas.Length).ToArray()).ToArray();
            var quantidades = Enumerable.Repeat(instancia.Escolas.Length, instancia.Candidatos.Length).ToArray();
            var primeiras = ordemEscolas.Select(o => o[0]).ToArray();
            var segundas = ordemEscolas.Select(o => o.Length > 1 ? o[1] : -1).ToArray();
            for (int passo = 0; passo < instancia.Candidatos.Length; passo++)
            {
                bool deterministica = tentativa == 1 && !aleatorizar;
                var prioridades = new List<(int Candidato, int Quantidade, double Arrependimento, double Desempate)>(4);
                bool bloqueada = false;
                for (int i = 0; i < instancia.Candidatos.Length; i++)
                {
                    if (solucao.Alocacao[i] >= 0) continue;
                    int quantidade = quantidades[i], primeira = primeiras[i], segunda = segundas[i];
                    // Consome o mesmo sorteio por candidato, inclusive em tentativas bloqueadas.
                    double desempate = random.NextDouble();
                    if (quantidade == 0) { bloqueada = true; continue; }
                    double arrependimento = quantidade == 1 ? double.PositiveInfinity
                        : instancia.Distancias[i][segunda] - instancia.Distancias[i][primeira];
                    prioridades.Add((i, quantidade, arrependimento, deterministica ? i : desempate));
                    prioridades.Sort((a, b) =>
                    {
                        int comparacao = (a.Quantidade == 1 ? 0 : 1).CompareTo(b.Quantidade == 1 ? 0 : 1);
                        if (comparacao == 0) comparacao = b.Arrependimento.CompareTo(a.Arrependimento);
                        if (comparacao == 0) comparacao = a.Desempate.CompareTo(b.Desempate);
                        return comparacao == 0 ? a.Candidato.CompareTo(b.Candidato) : comparacao;
                    });
                    if (prioridades.Count > 3) prioridades.RemoveAt(3);
                }
                if (bloqueada) { completa = false; break; }
                var escolhido = prioridades[deterministica ? 0 : random.Next(prioridades.Count)];
                var escolas = ordemEscolas[escolhido.Candidato]
                    .Where(j => solucao.PodeInserir(instancia, escolhido.Candidato, j, maxTipos)).ToArray();
                int escola = escolas[0];
                if ((tentativa > 1 || aleatorizar) && random.NextDouble() < 0.25)
                    escola = escolas[random.Next(Math.Min(3, escolas.Length))];
                solucao.Inserir(instancia, escolhido.Candidato, escola);
                historico?.Alocar(instancia, escolhido.Candidato, escola);
                for (int i = 0; i < instancia.Candidatos.Length; i++)
                {
                    if (solucao.Alocacao[i] >= 0 || !viaveis[i][escola]
                        || solucao.PodeInserir(instancia, i, escola, maxTipos)) continue;
                    viaveis[i][escola] = false;
                    quantidades[i]--;
                    if (primeiras[i] != escola && segundas[i] != escola) continue;
                    primeiras[i] = segundas[i] = -1;
                    foreach (int j in ordemEscolas[i])
                        if (viaveis[i][j])
                        {
                            if (primeiras[i] < 0) primeiras[i] = j;
                            else { segundas[i] = j; break; }
                        }
                }
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
        => BuscaLocalEficiente.Melhorar(instancia, solucao, maxTipos, usarTrocas, historico);

    // Preserva a versão de referência para conferir a equivalência da aceleração.
    public static (int Realocacoes, int Trocas) MelhorarReferencia(Instancia instancia, Solucao solucao, int maxTipos, bool usarTrocas = true, Historico? historico = null)
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
