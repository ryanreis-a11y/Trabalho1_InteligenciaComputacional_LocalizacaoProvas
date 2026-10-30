namespace LocalizacaoProvas;

public sealed record Passo(int Numero, int Tentativa, string Etapa, string Movimento,
    string Candidato, string OutroCandidato, string Origem, string Destino,
    double CustoAnteriorKm, double CustoPosteriorKm, string Observacao);

public sealed class Historico
{
    public List<Passo> Passos { get; } = [];
    private int tentativa;
    private double custo;

    // Reinicia o custo parcial e registra qual tentativa construtiva está começando.
    public void Iniciar(int numero, string estrategia)
    {
        tentativa = numero;
        custo = 0;
        Registrar("construcao", "inicio", "", "", "", "", 0, estrategia);
    }

    // Registra uma alocação com custo parcial, sem recalcular toda a solução.
    public void Alocar(Instancia instancia, int candidato, int escola) => Registrar("construcao", "alocacao",
        instancia.Candidatos[candidato].Id, "", "", instancia.Escolas[escola].Id,
        instancia.Distancias[candidato][escola], "Custo parcial: candidatos ainda podem estar sem escola.");

    // Marca o descarte ou a conclusão de uma construção para não confundir tentativas.
    public void Encerrar(bool sucesso) => Registrar("construcao", sucesso ? "concluida" : "descartada",
        "", "", "", "", 0, sucesso ? "Solução inicial completa e viável." : "Tentativa bloqueada; solução parcial descartada.");

    // Registra a transferência ou troca aceita com a redução efetiva do custo completo.
    public void Melhorar(Instancia instancia, int candidato, int outro, int origem, int destino, double delta) =>
        Registrar("busca", outro < 0 ? "realocacao" : "troca", instancia.Candidatos[candidato].Id,
            outro < 0 ? "" : instancia.Candidatos[outro].Id, instancia.Escolas[origem].Id,
            instancia.Escolas[destino].Id, delta, "Movimento viável e estritamente melhorante.");

    // Indica se a busca encerrou nas vizinhanças escolhidas ou foi desativada.
    public void Finalizar(string busca) => Registrar("busca", "fim", "", "", "", "", 0,
        busca == "nenhuma" ? "Busca desativada." : "Nenhuma melhoria nas vizinhanças utilizadas.");

    // Acumula os deltas e numera todos os eventos em ordem cronológica.
    private void Registrar(string etapa, string movimento, string candidato, string outro, string origem,
        string destino, double delta, string observacao)
    {
        double anterior = custo;
        custo += delta;
        Passos.Add(new Passo(Passos.Count, tentativa, etapa, movimento, candidato, outro, origem, destino,
            anterior, custo, observacao));
    }
}
