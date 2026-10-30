using System.Globalization;
using System.Text.RegularExpressions;

namespace LocalizacaoProvas;

public static class LeitorTxt
{
    // Interpreta as três seções do TXT da disciplina e soma a capacidade das salas.
    public static Instancia Ler(string caminho)
    {
        int quantidadeCandidatos = -1, quantidadeEscolas = -1;
        string secao = "";
        var candidatos = new List<Candidato>();
        var escolas = new List<Escola>();
        var distancias = new List<double[]>();
        int numeroLinha = 0;
        foreach (string original in File.ReadLines(caminho))
        {
            numeroLinha++;
            string linha = original.Trim().TrimStart('\uFEFF');
            if (linha.Length == 0) continue;
            try
            {
                if (linha.StartsWith("NUM_CANDIDATOS:") || linha.StartsWith("NUM_ESCOLAS:"))
                {
                    // Aceita cabeçalhos separados ou na mesma linha sem alterar os dados.
                    var camposCabecalho = Regex.Matches(linha, @"(NUM_CANDIDATOS|NUM_ESCOLAS):\s*(\d+)");
                    if (camposCabecalho.Count == 0) throw new FormatException("Cabeçalho numérico inválido.");
                    foreach (Match campo in camposCabecalho)
                    {
                        int quantidade = int.Parse(campo.Groups[2].Value, CultureInfo.InvariantCulture);
                        if (campo.Groups[1].Value == "NUM_CANDIDATOS") quantidadeCandidatos = quantidade;
                        else quantidadeEscolas = quantidade;
                    }
                    continue;
                }
                if (linha.StartsWith('#'))
                {
                    if (linha.Contains("MATRIZ_DISTANCIAS")) secao = "distancias";
                    else if (linha.Contains("CANDIDATOS")) secao = "candidatos";
                    else if (linha.Contains("ESCOLAS")) secao = "escolas";
                    continue;
                }
                string[] campos = linha.Split(',', StringSplitOptions.TrimEntries);
                switch (secao)
                {
                    case "candidatos":
                        if (campos.Length != 5) throw new FormatException("Candidato deve possuir cinco campos.");
                        candidatos.Add(new Candidato(campos[0], campos[2]));
                        break;
                    case "escolas":
                        if (campos.Length != 6) throw new FormatException("Escola deve possuir seis campos.");
                        int salas = int.Parse(campos[4], CultureInfo.InvariantCulture);
                        int[] capacidades = campos[5].Split(';').Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToArray();
                        if (salas <= 0 || capacidades.Length != salas || capacidades.Any(c => c <= 0))
                            throw new FormatException("Quantidade ou capacidade de salas inválida.");
                        escolas.Add(new Escola(campos[0], capacidades.Sum()));
                        break;
                    case "distancias":
                        distancias.Add(campos.Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray());
                        break;
                    default: throw new FormatException("Formato TXT não reconhecido; esperadas as seções da disciplina.");
                }
            }
            catch (Exception erro) when (erro is FormatException or OverflowException)
            {
                throw new ArgumentException($"Linha {numeroLinha}: {erro.Message}", erro);
            }
        }
        if (candidatos.Count != quantidadeCandidatos || escolas.Count != quantidadeEscolas)
            throw new ArgumentException("Os totais declarados no cabeçalho não correspondem aos registros lidos.");
        var instancia = new Instancia { Candidatos = candidatos.ToArray(), Escolas = escolas.ToArray(), Distancias = distancias.ToArray() };
        instancia.Validar();
        return instancia;
    }
}
