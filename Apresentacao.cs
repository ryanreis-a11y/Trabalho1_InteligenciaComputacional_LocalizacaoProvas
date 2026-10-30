using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LocalizacaoProvas;

public static class Apresentacao
{
    // Exporta todos os eventos para planilha e uma apresentação HTML independente da internet.
    public static void Exportar(string pasta, Historico historico, Resultado resultado, string instancia)
    {
        var csv = new StringBuilder("passo;tentativa;etapa;movimento;candidato;outro_candidato;origem;destino;custo_anterior_km;custo_posterior_km;observacao\r\n");
        foreach (var p in historico.Passos)
            csv.AppendLine(string.Join(';', new[] { p.Numero.ToString(), p.Tentativa.ToString(), p.Etapa,
                p.Movimento, p.Candidato, p.OutroCandidato, p.Origem, p.Destino,
                p.CustoAnteriorKm.ToString("R", CultureInfo.InvariantCulture),
                p.CustoPosteriorKm.ToString("R", CultureInfo.InvariantCulture), p.Observacao }.Select(CampoCsv)));
        Program.EscreverArquivo(Path.Combine(pasta, "historico.csv"), csv.ToString());
        string grafico = Grafico(historico, resultado);
        Program.EscreverArquivo(Path.Combine(pasta, "evolucao.svg"), grafico);
        string dados = JsonSerializer.Serialize(historico.Passos, JsonConfig.Opcoes);
        string html = $$$"""
            <!doctype html><html lang="pt-BR"><meta charset="utf-8">
            <title>Execução — {{{WebUtility.HtmlEncode(instancia)}}}</title>
            <style>
            body{font:16px system-ui;margin:32px;color:#183044;background:#f3f6fa}main{max-width:1200px;margin:auto}
            h1{font-size:28px}.painel{background:white;padding:24px;border-radius:12px;margin:20px 0;box-shadow:0 2px 8px #0001}
            .metricas{display:flex;gap:32px;flex-wrap:wrap}.metricas strong{display:block;font-size:24px;color:#006b70}
            table{border-collapse:collapse;width:100%;font-size:13px}th,td{text-align:left;padding:10px;border-bottom:1px solid #dce3e9}
            th{background:#eaf1f6}button,select{padding:8px;margin:8px}svg{width:100%;height:auto}.tabela{overflow:auto}
            @media print{button,select{display:none}body{background:white;margin:0}.painel{box-shadow:none}}
            </style><main><h1>{{{WebUtility.HtmlEncode(instancia)}}}</h1><p>Semente: {{{resultado.Seed}}} · Histórico da execução</p>
            <section class="painel metricas">
            <div>Inicial<strong>{{{resultado.InicialKm:F4}}} km</strong></div><div>Final<strong>{{{resultado.FinalKm:F4}}} km</strong></div>
            <div>Melhoria<strong>{{{resultado.MelhoriaPercentual:F2}}}%</strong></div><div>Escolas<strong>{{{resultado.EscolasUsadas}}}</strong></div>
            </section><section class="painel"><h2>Evolução da busca local</h2>{{{grafico}}}
            <p>Movimento 0 = solução inicial completa. A construção tem custo parcial e está separada na tabela.</p></section>
            <section class="painel"><h2>Passo a passo</h2>
            <label>Etapa <select id="etapa"><option value="">Todas</option><option value="construcao">Construção</option><option value="busca">Busca local</option></select></label>
            <label>Tentativa <select id="tentativa"><option value="">Todas</option></select></label>
            <p>Tentativas descartadas são registradas explicitamente. Na troca, o segundo candidato faz o caminho inverso.</p>
            <div class="tabela"><table><thead><tr><th>Passo</th><th>Tentativa</th><th>Etapa</th><th>Ação</th><th>Candidato</th><th>Outro</th><th>Origem</th><th>Destino</th><th>Antes (km)</th><th>Depois (km)</th><th>Observação</th></tr></thead><tbody id="linhas"></tbody></table></div>
            <button id="anterior">Anterior</button><span id="pagina"></span><button id="proxima">Próxima</button>
            </section></main><script>
            const dados={{{dados}}};
            const etapa=document.getElementById('etapa'), tentativa=document.getElementById('tentativa');
            let pagina=0; const tamanho=50;
            for(const n of [...new Set(dados.map(p=>p.tentativa))]){const o=document.createElement('option');o.value=n;o.textContent=n;tentativa.append(o);}
            function render(){
              const lista=dados.filter(p=>(!etapa.value||p.etapa===etapa.value)&&(!tentativa.value||p.tentativa===Number(tentativa.value)));
              const total=Math.max(1,Math.ceil(lista.length/tamanho));pagina=Math.max(0,Math.min(pagina,total-1));
              const corpo=document.getElementById('linhas');corpo.replaceChildren();
              for(const p of lista.slice(pagina*tamanho,(pagina+1)*tamanho)){
                const tr=document.createElement('tr');
                for(const v of [p.numero,p.tentativa,p.etapa,p.movimento,p.candidato,p.outroCandidato,p.origem,p.destino,p.custoAnteriorKm.toFixed(4),p.custoPosteriorKm.toFixed(4),p.observacao]){
                  const td=document.createElement('td');td.textContent=v;tr.append(td);
                }corpo.append(tr);
              }
              document.getElementById('pagina').textContent=`Página ${pagina+1} de ${total} · ${lista.length} eventos`;
              document.getElementById('anterior').disabled=pagina===0;document.getElementById('proxima').disabled=pagina===total-1;
            }
            etapa.onchange=tentativa.onchange=()=>{pagina=0;render();};
            document.getElementById('anterior').onclick=()=>{pagina--;render();};
            document.getElementById('proxima').onclick=()=>{pagina++;render();};render();
            </script></html>
            """;
        Program.EscreverArquivo(Path.Combine(pasta, "apresentacao.html"), html);
    }

    // Produz um gráfico vetorial exportável com o custo completo após cada melhoria aceita.
    private static string Grafico(Historico historico, Resultado resultado)
    {
        var custos = new[] { resultado.InicialKm }.Concat(historico.Passos
            .Where(p => p.Etapa == "busca" && p.Movimento is "realocacao" or "troca")
            .Select(p => p.CustoPosteriorKm)).ToArray();
        double maximo = Math.Max(1, custos.Max() * 1.05);
        string pontos = string.Join(' ', custos.Select((c, i) => FormattableString.Invariant(
            $"{85.0 + 850.0 * i / Math.Max(1, custos.Length - 1):F3},{310 - 250 * c / maximo:F3}")));
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 380\" role=\"img\" aria-label=\"Distância total após cada movimento aceito\"><rect width=\"1000\" height=\"380\" fill=\"white\"/>");
        for (int i = 0; i <= 4; i++)
        {
            double y = 310 - i * 62.5;
            svg.Append(FormattableString.Invariant($"<path d=\"M85 {y} H935\" stroke=\"#dce3e9\"/><text x=\"75\" y=\"{y + 5}\" text-anchor=\"end\" font-size=\"13\">{maximo * i / 4:F1}</text>"));
        }
        svg.Append($"<polyline points=\"{pontos}\" fill=\"none\" stroke=\"#007d83\" stroke-width=\"3\"/>");
        svg.Append(FormattableString.Invariant($"<circle cx=\"85\" cy=\"{310 - 250 * custos[0] / maximo:F3}\" r=\"4\" fill=\"#007d83\"/>"));
        svg.Append($"<g font-family=\"sans-serif\" fill=\"#183044\"><text x=\"85\" y=\"335\">0</text><text x=\"935\" y=\"335\" text-anchor=\"end\">{custos.Length - 1}</text><text x=\"500\" y=\"365\" text-anchor=\"middle\">Movimentos aceitos</text><text x=\"85\" y=\"30\">Distância total (km)</text></g></svg>");
        return svg.ToString();
    }

    // Protege separadores e aspas dos identificadores no CSV.
    private static string CampoCsv(string valor) => "\"" + valor.Replace("\"", "\"\"") + "\"";
}
