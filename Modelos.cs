using System.Text.Json;

namespace LocalizacaoProvas;

public sealed record Escola(string Id, int Capacidade);
public sealed record Candidato(string Id, string Tipo);

public sealed class Instancia
{
    public Escola[] Escolas { get; init; } = [];
    public Candidato[] Candidatos { get; init; } = [];
    public double[][] Distancias { get; init; } = [];

    // Lê a matriz de distâncias fornecida, sem consultar serviços de CEP.
    public static Instancia Ler(string caminho)
    {
        if (Path.GetExtension(caminho).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return LeitorTxt.Ler(caminho);
        var instancia = JsonSerializer.Deserialize<Instancia>(File.ReadAllText(caminho), JsonConfig.Opcoes)
            ?? throw new ArgumentException("O arquivo não contém uma instância.");
        instancia.Validar();
        return instancia;
    }

    // Rejeita dimensões, identificadores, capacidades e distâncias inconsistentes.
    public void Validar()
    {
        if (Escolas is null || Candidatos is null || Distancias is null || Escolas.Length == 0 || Candidatos.Length == 0)
            throw new ArgumentException("Informe escolas, candidatos e distâncias não vazios.");
        if (Escolas.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id) || e.Capacidade <= 0)
            || Candidatos.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Tipo)))
            throw new ArgumentException("IDs e tipos devem ser preenchidos; capacidades devem ser positivas.");
        if (Escolas.Select(e => e.Id).Distinct().Count() != Escolas.Length
            || Candidatos.Select(c => c.Id).Distinct().Count() != Candidatos.Length)
            throw new ArgumentException("IDs de escolas e de candidatos devem ser únicos em seus respectivos conjuntos.");
        if (Distancias.Length != Candidatos.Length || Distancias.Any(l => l is null || l.Length != Escolas.Length
            || l.Any(d => !double.IsFinite(d) || d < 0)))
            throw new ArgumentException("Distâncias: uma linha por candidato e uma coluna por escola; valores finitos e não negativos.");
    }
}

public static class JsonConfig
{
    public static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed class Solucao
{
    public int[] Alocacao { get; }
    public int[] Ocupacao { get; }
    public Dictionary<string, int>[] Tipos { get; }

    // Inicia a solução parcial com todos os candidatos ainda sem escola.
    public Solucao(Instancia instancia)
    {
        Alocacao = Enumerable.Repeat(-1, instancia.Candidatos.Length).ToArray();
        Ocupacao = new int[instancia.Escolas.Length];
        Tipos = instancia.Escolas.Select(_ => new Dictionary<string, int>()).ToArray();
    }

    // Verifica capacidade compartilhada e o limite de tipos distintos da escola.
    public bool PodeInserir(Instancia instancia, int candidato, int escola, int maxTipos)
    {
        return Ocupacao[escola] < instancia.Escolas[escola].Capacidade
            && (Tipos[escola].ContainsKey(instancia.Candidatos[candidato].Tipo) || Tipos[escola].Count < maxTipos);
    }

    // Insere um candidato e mantém os contadores usados na avaliação dos movimentos.
    public void Inserir(Instancia instancia, int candidato, int escola)
    {
        Alocacao[candidato] = escola;
        Ocupacao[escola]++;
        var tipo = instancia.Candidatos[candidato].Tipo;
        Tipos[escola][tipo] = Tipos[escola].GetValueOrDefault(tipo) + 1;
    }

    // Remove a alocação; o último candidato de um tipo libera esse tipo na escola.
    public void Remover(Instancia instancia, int candidato)
    {
        int escola = Alocacao[candidato];
        var tipo = instancia.Candidatos[candidato].Tipo;
        Ocupacao[escola]--;
        if (--Tipos[escola][tipo] == 0) Tipos[escola].Remove(tipo);
        Alocacao[candidato] = -1;
    }

    // Calcula a distância total, exigindo uma alocação completa.
    public double Custo(Instancia instancia) => Alocacao.Select((escola, i) => instancia.Distancias[i][escola]).Sum();

    // Reconstrói os contadores de forma independente para conferir a solução final.
    public void Validar(Instancia instancia, int maxTipos)
    {
        var ocupacao = new int[instancia.Escolas.Length];
        var tipos = instancia.Escolas.Select(_ => new Dictionary<string, int>()).ToArray();
        for (int i = 0; i < Alocacao.Length; i++)
        {
            int j = Alocacao[i];
            if (j < 0 || j >= instancia.Escolas.Length) throw new InvalidOperationException("Candidato sem escola válida.");
            ocupacao[j]++;
            string tipo = instancia.Candidatos[i].Tipo;
            tipos[j][tipo] = tipos[j].GetValueOrDefault(tipo) + 1;
        }
        for (int j = 0; j < ocupacao.Length; j++)
            if (ocupacao[j] > instancia.Escolas[j].Capacidade || tipos[j].Count > maxTipos
                || ocupacao[j] != Ocupacao[j] || tipos[j].Count != Tipos[j].Count
                || tipos[j].Any(t => Tipos[j].GetValueOrDefault(t.Key) != t.Value))
                throw new InvalidOperationException("Violação de capacidade, tipos ou contadores internos.");
    }
}
