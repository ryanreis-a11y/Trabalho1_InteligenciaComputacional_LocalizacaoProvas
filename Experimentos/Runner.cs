using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalizacaoProvas;

internal static class Runner
{
    // Executa o protocolo em rodadas balanceadas e conserva cada resultado imediatamente.
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string raiz = Catalogo.Raiz();
        if (args.Contains("--validar")) { ValidarEquivalencia(); return; }
        bool piloto = args.Contains("--piloto");
        int paralelismo = args.Contains("--paralelismo") ? int.Parse(args[Array.IndexOf(args, "--paralelismo") + 1]) : 1;
        if (paralelismo < 1) throw new ArgumentException("Paralelismo deve ser positivo.");
        var sincronizacao = new object();
        string pasta = Path.Combine(raiz, "resultados", "comparacao_2026_10_30_definitivo");
        Directory.CreateDirectory(pasta);
        string csv = Path.Combine(pasta, "execucoes.csv");
        bool retomar = File.Exists(csv);
        int sementeBase = retomar ? JsonDocument.Parse(File.ReadAllText(Path.Combine(pasta, "protocolo.json")))
            .RootElement.GetProperty("sementeBase").GetInt32() : RandomNumberGenerator.GetInt32(1, 1_000_000_000);
        var cargas = Directory.GetFiles(Path.Combine(raiz, "instancias"), "*.txt")
            .Select(p => (Nome: Path.GetFileNameWithoutExtension(p), Arquivo: p, Dados: Instancia.Ler(p)))
            .OrderBy(p => p.Dados.Candidatos.Length).ThenBy(p => p.Nome).ToArray();
        var concluidos = retomar ? File.ReadAllLines(csv).Skip(1).Select(l => l.Split(';'))
            .Select(c => $"{c[0]}/{c[1]}/{c[2]}").ToHashSet() : [];
        if (!retomar) File.WriteAllText(csv, "instancia;max_tipos;repeticao;seed;status;inicial_km;final_km;melhoria_pct;construcao_ms;busca_ms;escolas;tentativa;realocacoes;trocas;erro\r\n", new UTF8Encoding(false));
        var relogioLote = Stopwatch.StartNew();
        int alvo = piloto ? 1 : 103;
        double duracaoAnterior = retomar && File.Exists(Path.Combine(pasta, "estado.json"))
            ? JsonDocument.Parse(File.ReadAllText(Path.Combine(pasta, "estado.json"))).RootElement.GetProperty("segundos").GetDouble() : 0;
        Escrever(Path.Combine(pasta, "protocolo.json"), new { sementeBase, tentativas = 100, alvoInicial = 103,
            alvoAposCincoMinutos = 53, aleatorizar = true, historicoNoTempo = false, repeticoesPiloto = 3, repeticoesPrincipais = 50,
            branchBase = "correcoes", commitBase = "191b41d2b2fdd20f1683b6dc35206e1faa89c872", cpu = Environment.ProcessorCount,
            paralelismo, runtime = Environment.Version.ToString(), sistema = Environment.OSVersion.ToString(), inicioUtc = DateTimeOffset.UtcNow,
            arquivos = cargas.Select(c => new { c.Nome, candidatos = c.Dados.Candidatos.Length, escolas = c.Dados.Escolas.Length,
                tipos = c.Dados.Candidatos.Select(x => x.Tipo).Distinct().Count(), capacidade = c.Dados.Escolas.Sum(x => x.Capacidade),
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(c.Arquivo))) }) });
        var trabalhos = Enumerable.Range(0, piloto ? 1 : 103)
            .SelectMany(repeticao => cargas.Select(carga => (repeticao, carga)));
        Parallel.ForEach(trabalhos, new ParallelOptions { MaxDegreeOfParallelism = paralelismo }, trabalho =>
        {
                int repeticao = trabalho.repeticao;
                var carga = trabalho.carga;
                if (!piloto && relogioLote.Elapsed.TotalSeconds + duracaoAnterior > 300) Interlocked.Exchange(ref alvo, 53);
                if (repeticao >= Volatile.Read(ref alvo)) return;
                for (int maxTipos = 1; maxTipos <= 2; maxTipos++)
                {
                    string chave = $"{carga.Nome}/{maxTipos}/{repeticao}";
                    lock (sincronizacao) { if (concluidos.Contains(chave)) continue; }
                    int seed = sementeBase + repeticao;
                    var cronometro = Stopwatch.StartNew();
                    double construcao = 0;
                    try
                    {
                        var (solucao, tentativa) = Heuristica.Construir(carga.Dados, maxTipos, seed, 100, null, true);
                        construcao = cronometro.Elapsed.TotalMilliseconds;
                        double inicial = solucao.Custo(carga.Dados);
                        cronometro.Restart();
                        var movimentos = Heuristica.Melhorar(carga.Dados, solucao, maxTipos);
                        double busca = cronometro.Elapsed.TotalMilliseconds;
                        solucao.Validar(carga.Dados, maxTipos);
                        double final = solucao.Custo(carga.Dados);
                        if (final > inicial + 1e-6) throw new Exception("Busca piorou a solução.");
                        lock (sincronizacao) File.AppendAllText(csv, FormattableString.Invariant($"{carga.Nome};{maxTipos};{repeticao};{seed};ok;{inicial:R};{final:R};{(inicial == 0 ? 0 : 100 * (inicial - final) / inicial):R};{construcao:R};{busca:R};{solucao.Ocupacao.Count(x => x > 0)};{tentativa};{movimentos.Realocacoes};{movimentos.Trocas};\r\n"));
                        if (repeticao == 0)
                            Escrever(Path.Combine(pasta, $"{carga.Nome}_tipos_{maxTipos}_representativa.json"), new
                            { seed, inicial, final, alocacao = solucao.Alocacao, ocupacao = solucao.Ocupacao, tipos = solucao.Tipos });
                        Console.WriteLine($"{concluidos.Count + 1}: {carga.Nome} tipos={maxTipos} rep={repeticao + 1} custo={final:F2} tempo={construcao + busca:F0}ms");
                    }
                    catch (Exception erro) when (erro is ArgumentException or InvalidOperationException)
                    {
                        double tempo = cronometro.Elapsed.TotalMilliseconds;
                        string mensagem = erro.Message.Replace(';', ',').ReplaceLineEndings(" ");
                        lock (sincronizacao) File.AppendAllText(csv, FormattableString.Invariant($"{carga.Nome};{maxTipos};{repeticao};{seed};falha;;;;{tempo:R};0;;;;;{mensagem}\r\n"));
                        Console.WriteLine($"{concluidos.Count + 1}: {carga.Nome} tipos={maxTipos} rep={repeticao + 1} FALHA ({tempo:F0}ms)");
                    }
                    lock (sincronizacao)
                    {
                        concluidos.Add(chave);
                        Escrever(Path.Combine(pasta, "estado.json"), new { completos = concluidos.Count, alvoPorConfiguracao = alvo,
                            segundos = relogioLote.Elapsed.TotalSeconds + duracaoAnterior, ultimaConfiguracao = chave, concluido = false });
                    }
                }
        });
        Escrever(Path.Combine(pasta, "estado.json"), new { completos = concluidos.Count, alvoPorConfiguracao = alvo,
            segundos = relogioLote.Elapsed.TotalSeconds + duracaoAnterior, concluido = true });
    }

    // Compara a solução e os movimentos da busca rápida com a referência em exemplos gerais.
    private static void ValidarEquivalencia()
    {
        var random = new Random(81731);
        for (int caso = 0; caso < 1000; caso++)
        {
            int n = random.Next(2, 35), m = random.Next(2, 7), limite = caso % 2 + 1;
            var escolas = Enumerable.Range(0, m).Select(j => new Escola($"E{j}", n)).ToArray();
            var candidatos = Enumerable.Range(0, n).Select(i => new Candidato($"C{i}", $"T{i % m}")).ToArray();
            var instancia = new Instancia { Escolas = escolas, Candidatos = candidatos,
                Distancias = Enumerable.Range(0, n).Select(_ => Enumerable.Range(0, m).Select(_ => random.NextDouble() * 20).ToArray()).ToArray() };
            var a = new Solucao(instancia); var b = new Solucao(instancia);
            for (int i = 0; i < n; i++) { a.Inserir(instancia, i, i % m); b.Inserir(instancia, i, i % m); }
            var referencia = Heuristica.MelhorarReferencia(instancia, a, limite);
            var eficiente = Heuristica.Melhorar(instancia, b, limite);
            if (referencia != eficiente || !a.Alocacao.SequenceEqual(b.Alocacao))
                throw new Exception($"Equivalência falhou no caso {caso}.");
        }
        for (int caso = 0; caso < 150; caso++)
        {
            int n = random.Next(6, 45), m = random.Next(3, 9), limite = caso % 2 + 1;
            var instancia = new Instancia {
                Escolas = Enumerable.Range(0, m).Select(j => new Escola($"E{j}", random.Next(4, 15))).ToArray(),
                Candidatos = Enumerable.Range(0, n).Select(i => new Candidato($"C{i}", $"T{i % 3}")).ToArray(),
                Distancias = Enumerable.Range(0, n).Select(_ => Enumerable.Range(0, m).Select(_ => (double)random.Next(1, 30)).ToArray()).ToArray()
            };
            (Solucao Solucao, int Tentativa)? a = null, b = null;
            try { a = HeuristicaReferencia.Construir(instancia, limite, caso, 10, null, caso % 3 != 0); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
            try { b = Heuristica.Construir(instancia, limite, caso, 10, null, caso % 3 != 0); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
            if (a.HasValue != b.HasValue || (a.HasValue && (a.Value.Tentativa != b!.Value.Tentativa
                || !a.Value.Solucao.Alocacao.SequenceEqual(b.Value.Solucao.Alocacao))))
                throw new Exception($"Construção divergiu no caso {caso}.");
        }
        Console.WriteLine("1000 buscas e 150 construções: resultados idênticos à referência.");
    }

    // Mantém metadados legíveis e terminações de linha Windows.
    private static void Escrever(string caminho, object dados) => File.WriteAllText(caminho,
        JsonSerializer.Serialize(dados, JsonConfig.Opcoes).ReplaceLineEndings("\r\n") + "\r\n", new UTF8Encoding(false));
}
