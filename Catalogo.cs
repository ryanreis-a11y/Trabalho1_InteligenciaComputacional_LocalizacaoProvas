namespace LocalizacaoProvas;

public static class Catalogo
{
    // Separa os resultados pelo número do conjunto no nome, independentemente do limite de tipos.
    public static string Conjunto(string nome)
    {
        var correspondencia = System.Text.RegularExpressions.Regex.Match(nome, @"^instancia_([12])_g\d+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return correspondencia.Success ? $"instancias_{correspondencia.Groups[1].Value}" : "outras_instancias";
    }

    // Localiza o projeto mesmo quando o programa é iniciado pelo Visual Studio em bin/.
    public static string Raiz()
    {
        foreach (string inicio in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var pasta = new DirectoryInfo(inicio); pasta is not null; pasta = pasta.Parent)
                if (File.Exists(Path.Combine(pasta.FullName, "LocalizacaoProvas.csproj"))) return pasta.FullName;
        return AppContext.BaseDirectory;
    }

    // Aceita nome sem extensão do catálogo ou caminho explícito para uma entrada externa.
    public static string Resolver(string nome)
    {
        if (File.Exists(nome)) return Path.GetFullPath(nome);
        string pasta = Path.Combine(Raiz(), "instancias");
        string[] candidatos = [nome, nome + ".txt", nome + ".json"];
        foreach (string candidato in candidatos)
        {
            if (Path.GetFileName(candidato) != candidato) continue;
            string caminho = Path.Combine(pasta, candidato);
            if (File.Exists(caminho)) return caminho;
        }
        throw new ArgumentException($"Instância '{nome}' não encontrada. Use --listar-instancias para consultar os nomes.");
    }

    // Lista somente arquivos de instância executáveis, sem incluir o catálogo de procedência.
    public static void Listar()
    {
        string pasta = Path.Combine(Raiz(), "instancias");
        if (!Directory.Exists(pasta)) throw new ArgumentException("Pasta instancias não encontrada.");
        foreach (string arquivo in Directory.EnumerateFiles(pasta).Where(p => Path.GetExtension(p) is ".txt" or ".json").Order())
            Console.WriteLine(Path.GetFileNameWithoutExtension(arquivo));
    }
}
