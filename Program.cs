using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LocalizacaoProvas;

public static class Program
{
    // Coordena leitura, construção, busca e exportação; erros geram código de saída não zero.
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 0 || args.Contains("--help")) { Ajuda(); return 0; }
            if (args.SequenceEqual(new[] { "--listar-instancias" })) { Catalogo.Listar(); return 0; }
            var opcoes = Opcoes.Ler(args);
            opcoes = opcoes with { Instancia = Catalogo.Resolver(opcoes.Instancia) };
            string nome = Path.GetFileNameWithoutExtension(opcoes.Instancia);
            opcoes = opcoes with { Saida = Path.Combine(Catalogo.Raiz(), "resultados",
                Catalogo.Conjunto(nome), nome, TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "America/Sao_Paulo")
                    .ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture) + $"_tipos_{opcoes.MaxTipos}") };
            var instancia = Instancia.Ler(opcoes.Instancia);
            Directory.CreateDirectory(opcoes.Saida);
            var resultados = new List<Resultado>();
            int falhas = 0;
            for (int execucao = 0; execucao < opcoes.Execucoes; execucao++)
            {
                int seed = opcoes.SementeDaExecucao(execucao);
                string pastaExecucao = Path.Combine(opcoes.Saida, $"execucao_{execucao + 1:D2}_seed_{seed}");
                Directory.CreateDirectory(pastaExecucao);
                var historico = new Historico();
                try
                {
                    var relogio = Stopwatch.StartNew();
                    var (solucao, tentativa) = Heuristica.Construir(instancia, opcoes.MaxTipos, seed, opcoes.Tentativas, historico, aleatorizar: true);
                    double tempoConstrucao = relogio.Elapsed.TotalMilliseconds;
                    double inicial = solucao.Custo(instancia);
                    int[] alocacaoInicial = (int[])solucao.Alocacao.Clone();
                    relogio.Restart();
                    var movimentos = opcoes.Busca == "nenhuma" ? (Realocacoes: 0, Trocas: 0)
                        : Heuristica.Melhorar(instancia, solucao, opcoes.MaxTipos, opcoes.Busca == "vnd", historico);
                    double tempoBusca = relogio.Elapsed.TotalMilliseconds;
                    historico.Finalizar(opcoes.Busca);
                    solucao.Validar(instancia, opcoes.MaxTipos);
                    double final = solucao.Custo(instancia);
                    var resultado = new Resultado(seed, inicial, final, inicial == 0 ? 0 : 100 * (inicial - final) / inicial,
                        tempoConstrucao, tempoBusca, solucao.Ocupacao.Count(n => n > 0), tentativa,
                        movimentos.Realocacoes, movimentos.Trocas);
                    resultados.Add(resultado);
                    string arquivo = Path.Combine(pastaExecucao, "solucao.json");
                    var exportacao = new
                    {
                        instancia = Path.GetFullPath(opcoes.Instancia), opcoes.MaxTipos, opcoes.Busca, opcoes.Tentativas,
                        resultado,
                        aleatorizar = true,
                        historico = historico.Passos,
                        alocacoes = instancia.Candidatos.Select((c, i) => new
                        {
                            candidato = c.Id, c.Tipo, escolaInicial = instancia.Escolas[alocacaoInicial[i]].Id,
                            escolaFinal = instancia.Escolas[solucao.Alocacao[i]].Id,
                            distanciaKm = instancia.Distancias[i][solucao.Alocacao[i]]
                        }),
                        escolas = instancia.Escolas.Select((e, j) => new
                        {
                            e.Id, e.Capacidade, ocupacao = solucao.Ocupacao[j],
                            utilizacaoPercentual = 100.0 * solucao.Ocupacao[j] / e.Capacidade, tipos = solucao.Tipos[j]
                        })
                    };
                    EscreverArquivo(arquivo, JsonSerializer.Serialize(exportacao, JsonConfig.Opcoes));
                    Apresentacao.Exportar(pastaExecucao, historico, resultado, nome);
                    Console.WriteLine($"Seed {seed}: {inicial:F4} -> {final:F4} km; melhoria {resultado.MelhoriaPercentual:F2}%; escolas {resultado.EscolasUsadas}; tempo {tempoConstrucao + tempoBusca:F2} ms; tentativa {tentativa}.");
                }
                catch (Exception erro) when (erro is InvalidOperationException or ArgumentException)
                {
                    falhas++;
                    EscreverArquivo(Path.Combine(pastaExecucao, "falha.json"),
                        JsonSerializer.Serialize(new { seed, erro = erro.Message, historico = historico.Passos }, JsonConfig.Opcoes));
                    Console.Error.WriteLine($"Seed {seed}: {erro.Message}");
                }
            }
            ExportarResumo(opcoes, resultados, falhas);
            return falhas > 0 ? 2 : 0;
        }
        catch (Exception erro) when (erro is ArgumentException or IOException or UnauthorizedAccessException or JsonException or OverflowException)
        {
            Console.Error.WriteLine($"Erro: {erro.Message}");
            return 1;
        }
    }

    // Gera CSV por execução e estatísticas de variabilidade apenas das execuções bem-sucedidas.
    private static void ExportarResumo(Opcoes opcoes, List<Resultado> resultados, int falhas)
    {
        var csv = new StringBuilder("seed,inicial_km,final_km,melhoria_percentual,construcao_ms,busca_ms,escolas,tentativa,realocacoes,trocas\r\n");
        foreach (var r in resultados)
            csv.AppendLine(FormattableString.Invariant($"{r.Seed},{r.InicialKm},{r.FinalKm},{r.MelhoriaPercentual},{r.ConstrucaoMs},{r.BuscaMs},{r.EscolasUsadas},{r.Tentativa},{r.Realocacoes},{r.Trocas}"));
        EscreverArquivo(Path.Combine(opcoes.Saida, "resumo.csv"), csv.ToString());
        double? media = resultados.Count == 0 ? null : resultados.Average(r => r.FinalKm);
        double? desvio = resultados.Count == 0 ? null : Math.Sqrt(resultados.Average(r => Math.Pow(r.FinalKm - media!.Value, 2)));
        var resumo = new
        {
            opcoes.Instancia, opcoes.MaxTipos, opcoes.Busca, solicitadas = opcoes.Execucoes,
            sucessos = resultados.Count, falhas, mediaFinalKm = media, desvioPadraoPopulacionalKm = desvio,
            melhorFinalKm = resultados.Count == 0 ? (double?)null : resultados.Min(r => r.FinalKm)
        };
        EscreverArquivo(Path.Combine(opcoes.Saida, "resumo.json"), JsonSerializer.Serialize(resumo, JsonConfig.Opcoes));
        Console.WriteLine($"Concluído: {resultados.Count} sucesso(s), {falhas} falha(s). Saída: {Path.GetFullPath(opcoes.Saida)}");
    }

    // Padroniza todas as saídas em UTF-8 sem BOM e terminações Windows (CRLF).
    internal static void EscreverArquivo(string caminho, string conteudo)
    {
        string normalizado = conteudo.ReplaceLineEndings("\r\n");
        if (!normalizado.EndsWith("\r\n", StringComparison.Ordinal)) normalizado += "\r\n";
        File.WriteAllText(caminho, normalizado, new UTF8Encoding(false));
    }

    // Exibe os parâmetros necessários para reproduzir um experimento.
    private static void Ajuda() => Console.WriteLine("""
        Localização de locais de prova - C# / .NET 8
        dotnet run -- --instancia instancia_1_g08 --max-tipos 1
          --seed 42             Opcional: fixa a semente inicial; padrão aleatório por execução.
          --tentativas 100      Limite de reinícios da construção.
          --execucoes 1         Quantidade de execuções; cada uma registra sua semente.
          --busca vnd           vnd, realocacao ou nenhuma.
        Resultados: resultados/instancias_1 ou instancias_2/nome/rodada_tipos_N/execucao.
        --listar-instancias     Lista os nomes disponíveis na pasta instancias.
        --max-tipos é obrigatório e aceita somente 1 ou 2.
        --help                 Exibe esta ajuda.
        """);
}

public sealed record Resultado(int Seed, double InicialKm, double FinalKm, double MelhoriaPercentual,
    double ConstrucaoMs, double BuscaMs, int EscolasUsadas, int Tentativa, int Realocacoes, int Trocas);

public sealed record Opcoes(string Instancia, int MaxTipos, int? Seed, int Tentativas, int Execucoes, string Busca, string Saida)
{
    // Sorteia uma semente por execução; a opção explícita mantém a sequência reproduzível.
    public int SementeDaExecucao(int execucao)
    {
        if (execucao < 0 || execucao >= Execucoes) throw new ArgumentOutOfRangeException(nameof(execucao));
        return Seed.HasValue ? checked(Seed.Value + execucao) : Random.Shared.Next();
    }

    // Rejeita opções desconhecidas ou duplicadas para evitar executar configurações equivocadas.
    public static Opcoes Ler(string[] args)
    {
        string[] permitidas = ["--instancia", "--max-tipos", "--seed", "--tentativas", "--execucoes", "--busca"];
        var valores = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!permitidas.Contains(args[i]) || i + 1 >= args.Length || !valores.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Parâmetro desconhecido, repetido ou sem valor: {args[i]}");
        }
        if (!valores.TryGetValue("--instancia", out string? arquivo) || !valores.ContainsKey("--max-tipos"))
            throw new ArgumentException("Informe --instancia e --max-tipos. Use --help para ajuda.");
        var opcoes = new Opcoes(arquivo, Inteiro(valores, "--max-tipos", 0),
            valores.ContainsKey("--seed") ? Inteiro(valores, "--seed", 0) : null,
            Inteiro(valores, "--tentativas", 100), Inteiro(valores, "--execucoes", 1),
            valores.GetValueOrDefault("--busca", "vnd"), "resultados");
        if (opcoes.MaxTipos is not (1 or 2) || opcoes.Tentativas < 1 || opcoes.Execucoes < 1
            || opcoes.Busca is not ("vnd" or "realocacao" or "nenhuma"))
            throw new ArgumentException("Use max-tipos 1/2, tentativas/execuções positivas e busca vnd/realocacao/nenhuma.");
        if (opcoes.Seed.HasValue) _ = checked(opcoes.Seed.Value + (opcoes.Execucoes - 1));
        return opcoes;
    }

    // Converte parâmetros numéricos com uma mensagem de erro legível.
    private static int Inteiro(Dictionary<string, string> valores, string chave, int padrao)
    {
        if (!valores.TryGetValue(chave, out string? texto)) return padrao;
        if (!int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor))
            throw new ArgumentException($"{chave} deve receber um número inteiro.");
        return valor;
    }
}
