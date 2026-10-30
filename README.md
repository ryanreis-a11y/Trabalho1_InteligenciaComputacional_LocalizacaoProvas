# Localização de locais de prova

Implementação experimental em **C# / .NET 8**, sem pacotes externos. O problema é minimizar a soma das distâncias dos candidatos às escolas, respeitando capacidade e o limite parametrizado de tipos de prova por escola.

O algoritmo usa somente os dados da entrada: não contém nomes de provas, números de candidatos, capacidades, escolas ou regras específicas das instâncias de teste. As escolas utilizadas são aquelas que recebem pelo menos um candidato. Não é cobrado custo de abertura de escola porque esse custo não foi definido no enunciado.

## Executar

Abra `LocalizacaoProvas.sln` no Visual Studio ou use um terminal nesta pasta:

No Visual Studio, abra **Exibir → Terminal**. Execute os comandos na pasta que contém `LocalizacaoProvas.csproj`. É necessário ter o SDK do .NET 8 ou superior instalado.

```powershell
dotnet build -c Release
dotnet run -c Release -- --listar-instancias
dotnet run -c Release -- --instancia instancia_1_g08 --max-tipos 1
dotnet run -c Release -- --instancia instancia_1_g08 --max-tipos 2
```

Para realizar dez execuções com sementes sorteadas automaticamente:

```powershell
dotnet run -c Release -- --instancia instancia_2_g08 --max-tipos 2 --execucoes 10
```

Para consultar a ajuda: `dotnet run -c Release -- --help`.

`--max-tipos` é **obrigatório**. Em ambos os modos, a capacidade é compartilhada entre todos os candidatos da escola. O programa aloca candidatos às escolas, não a salas individuais. O TXT apresenta capacidades por sala, cuja soma define a capacidade da escola.

| Parâmetro | Uso | Padrão |
|---|---|---|
| `--instancia` | Nome do catálogo sem extensão ou caminho para TXT/JSON | Obrigatório |
| `--max-tipos` | Máximo de tipos por escola: `1` ou `2` | Obrigatório |
| `--seed` | Opcional: fixa a semente inicial para reproduzir resultados | Aleatória por execução |
| `--tentativas` | Máximo de tentativas para construir uma solução | `100` |
| `--execucoes` | Número de execuções; sorteia uma semente para cada uma, salvo `--seed` explícito | `1` |
| `--busca` | `nenhuma`, `realocacao` ou `vnd` | `vnd` |

Os resultados são organizados automaticamente em `resultados/instancias_1/<nome>/<rodada>/` ou `resultados/instancias_2/<nome>/<rodada>/`, conforme **o número da instância no nome do arquivo**, independentemente de `--max-tipos`. As duas pastas são irmãs: `instancia_1_g08` pertence a `instancias_1`, e `instancia_2_g08` pertence a `instancias_2`. Entradas externas sem esse padrão usam `resultados/outras_instancias/`. Cada rodada recebe data e hora de São Paulo com frações de segundo e o sufixo `_tipos_1` ou `_tipos_2`, evitando misturar execuções de comandos diferentes. Dentro dela, cada execução possui sua pasta com número e semente. Tempos medem o algoritmo, excluindo leitura da instância e escrita dos resultados; construção inclui as tentativas malsucedidas. A primeira execução pode ser mais lenta devido à compilação JIT.

### Organização automática dos resultados

Exemplo de estrutura gerada após executar instâncias dos dois conjuntos:

```text
resultados/
├── instancias_1/
│   └── instancia_1_g08/
│       └── <data_hora>_tipos_2/
│           ├── resumo.csv
│           ├── resumo.json
│           └── execucao_01_seed_<semente>/
│               ├── solucao.json
│               ├── historico.csv
│               ├── evolucao.svg
│               └── apresentacao.html
└── instancias_2/
    └── instancia_2_g08/
        └── <data_hora>_tipos_1/
            └── ...
```

`instancias_1` e `instancias_2` ficam no mesmo nível. O parâmetro `--max-tipos` define somente a restrição do algoritmo e o sufixo da rodada. Uma **rodada** corresponde a um comando; `--execucoes 10` cria dez pastas de execução dentro dela. O terminal informa o caminho da rodada ao concluir. A pasta de saída é fixa (`resultados`); não há parâmetro `--saida`.

## Entrada

### TXT da disciplina

O leitor reconhece cabeçalhos `NUM_CANDIDATOS` e `NUM_ESCOLAS`, tanto em linhas separadas quanto na mesma linha, e as seções:

```text
NUM_CANDIDATOS: 2
NUM_ESCOLAS: 2

# CANDIDATOS (id, CEP, tipo_prova, latitude, longitude)
C1, 00000001, A, -21.0, -43.0
C2, 00000002, B, -21.1, -43.1

# ESCOLAS (id, CEP, latitude, longitude, num_salas, capacidades_salas)
E1, 90000001, -21.0, -43.0, 2, 1;1
E2, 90000002, -21.2, -43.2, 1, 2

# MATRIZ_DISTANCIAS (linhas=candidatos, colunas=escolas, em km)
1.0, 10.0
1.0, 10.0
```

A ordem das linhas de candidatos e escolas define a ordem da matriz. O separador decimal é ponto. A matriz fornecida é a fonte das distâncias: CEP e coordenadas são metadados, não são usados para recalcular distâncias. Arquivos contendo apenas um caminho para outro computador não são instâncias válidas.

### JSON genérico

O formato JSON permite criar experimentos próprios sem CEP ou coordenadas:

```json
{
  "escolas": [{"id": "E1", "capacidade": 2}, {"id": "E2", "capacidade": 2}],
  "candidatos": [{"id": "C1", "tipo": "A"}, {"id": "C2", "tipo": "B"}],
  "distancias": [[1, 10], [1, 10]]
}
```

Use distâncias em quilômetros para manter a unidade indicada nas saídas. Identificadores e tipos são textos; qualquer nome de tipo é aceito. Capacidades são inteiros positivos, distâncias são finitas e não negativas. Cada candidato representa uma pessoa e ocupa uma vaga.

## Algoritmo e decisões

### Representação e avaliação

`Alocacao[i]` informa a escola do candidato `i`. Durante a construção, `-1` significa não alocado. Contadores mantêm ocupação e quantidade de candidatos por tipo em cada escola. Ao remover o último candidato de um tipo, esse tipo deixa de ocupar uma posição no limite da escola.

A função objetivo é `f(s) = soma(distancias[i][alocacao[i]])`. Somente soluções completas e viáveis são aceitas. Não há penalizações. Um validador independente reconstrói os contadores e confere capacidade, tipos e alocações ao final de cada fase.

### Construção

As tentativas alternam duas estratégias gerais, até encontrar a primeira solução viável:

1. **Tentativas ímpares: arrependimento por candidato.** Recalcula as escolas viáveis dos candidatos restantes. Prioriza candidatos com uma única opção e depois o maior arrependimento: distância da segunda melhor escola menos a distância da melhor. Aloca na escola viável mais próxima. Se algum candidato não tiver opção, abandona essa tentativa.
2. **Tentativas pares: concentração por tipo.** Ordena os grupos por demanda decrescente. Para cada grupo, escolhe a escola capaz de receber o maior lote de candidatos restantes; empates usam a menor distância média do lote. Preenche o lote com os candidatos mais próximos. Isso busca poupar posições de tipos nas escolas, ao custo de decisões geográficas potencialmente piores.

A primeira tentativa de cada estratégia é determinística. Nas ímpares posteriores, a escolha do candidato é sorteada entre os três primeiros na ordenação; com probabilidade de 25%, a escola é sorteada entre as três viáveis mais próximas. Nas pares posteriores, a prioridade dos grupos usa `demanda * (0.5 + U)`, com `U` uniforme em `[0, 1)`, e a escola é sorteada entre as três primeiras. Esses valores são fixos gerais, não calibrados com os arquivos fornecidos.

Reinícios descartam integralmente a construção parcial bloqueada. Não é usada busca exata para completar a solução. `--tentativas 1` testa somente a construção inicial por arrependimento. Sementes distintas podem gerar o mesmo resultado, especialmente se uma das duas primeiras tentativas determinísticas tiver sucesso.

**Limitação:** reinícios não garantem encontrar uma solução viável, mesmo quando ela existe. Falha da heurística não é prova de inviabilidade. O programa só declara inviabilidade diretamente em duas condições necessárias: capacidade total menor que a demanda ou tipos distintos excedendo `maxTipos * quantidadeEscolas`. Passar nessas verificações não garante viabilidade.

### Busca local

Usa primeira melhora, com ordem dos candidatos e escolas definida pela entrada:

- **Realocação:** mover um candidato para outra escola viável, com variação `d[i,b] - d[i,a]`.
- **Troca:** trocar as escolas de dois candidatos, com variação `d[i,b] + d[k,a] - d[i,a] - d[k,b]`. As duas remoções são consideradas antes de verificar os tipos das escolas. Uma troca inviável restaura o estado original.

`--busca realocacao` usa somente a primeira vizinhança. `--busca vnd` procura realocação e depois troca; após qualquer melhoria, reinicia pela realocação. `--busca nenhuma` preserva a solução construtiva. Só são aceitos movimentos com variação menor que `-1e-9`, para evitar problemas de arredondamento. O término indica ótimo local nas vizinhanças utilizadas, dentro dessa tolerância, não ótimo global.

**Caso de limitação para discutir:** com um tipo por escola e vários candidatos do mesmo tipo em cada escola, transferir o grupo inteiro pode melhorar muito, mas transferir ou trocar apenas uma pessoa pode violar o limite de tipos. A busca local não considera transferências coletivas, mudanças neutras nem movimentos temporariamente piores.

### Custo computacional

Com `n` candidatos e `m` escolas, a construção individual ordena até `m` opções por candidato a cada alocação: aproximadamente `O(n² m log(m))` por tentativa. A construção por grupos ordena candidatos para formar lotes. Uma varredura de realocação custa `O(nm)` e uma de troca custa `O(n²)`, assumindo operações médias constantes nos dicionários. Cada melhoria reinicia a varredura. A matriz ocupa `O(nm)` memória. Não há promessa de desempenho para entradas muito grandes.

## Experimentos e saídas

Exemplo para comparar as três abordagens com várias sementes, sem alterar o algoritmo:

```powershell
dotnet run -c Release -- --instancia instancia_1_g08 --max-tipos 1 --seed 42 --execucoes 10 --busca nenhuma
dotnet run -c Release -- --instancia instancia_1_g08 --max-tipos 1 --seed 42 --execucoes 10 --busca realocacao
dotnet run -c Release -- --instancia instancia_1_g08 --max-tipos 1 --seed 42 --execucoes 10 --busca vnd
```

Sem `--seed`, cada execução sorteia sua própria semente, que aparece no terminal, no nome da pasta de execução, nos dados exportados e no CSV. Para repetir uma execução, informe a semente registrada com `--seed NUMERO`. Com `--seed 42 --execucoes 10`, as sementes serão 42 a 51. O sorteio não garante sementes distintas nem soluções diferentes; as primeiras tentativas construtivas são determinísticas.

Repita com `--max-tipos 2`; o programa escolhe a pasta automaticamente. Para comparar buscas de forma pareada, mantenha a mesma instância, semente explícita e limite de tentativas. A busca local não consome números aleatórios, portanto parte da mesma solução inicial nessas condições.

Cada rodada exporta `resumo.csv` e `resumo.json` com os resultados e estatísticas apenas daquela rodada. Cada execução bem-sucedida possui:

- `solucao.json`: parâmetros, semente, alocações inicial/final, distribuição por escola e histórico cronológico completo.
- `historico.csv`: tabela de eventos, separada por ponto e vírgula, com custo anterior/posterior, candidato, escolas, tentativa e observação. Decimais usam ponto.
- `apresentacao.html`: página local, sem dependências externas, com indicadores, gráfico e tabela paginada de 50 eventos, filtrável por etapa e tentativa. Abra no navegador para apresentar.
- `evolucao.svg`: gráfico vetorial da busca local, que pode ser inserido nos slides.

O gráfico começa na solução completa inicial (movimento zero) e acompanha cada melhoria aceita. O custo parcial da construção aparece somente na tabela, separado por tentativa. Tentativas descartadas também são registradas; não devem ser interpretadas como soluções completas. A tabela registra ações realizadas, não cada vizinho rejeitado. Nenhum histórico muda os critérios da heurística. O custo adicional da coleta do histórico está incluído no tempo do algoritmo; HTML/CSV/SVG são gerados depois da medição.

Uma construção malsucedida gera `falha.json` com a semente, mensagem e eventos disponíveis. Não entra nas médias. Considere também a taxa de sucesso ao comparar configurações.

### O que abrir para analisar ou apresentar

| Objetivo | Arquivo |
|---|---|
| Apresentar uma execução com tabela e gráfico | `apresentacao.html`, aberto no navegador |
| Inserir a curva de evolução nos slides | `evolucao.svg` |
| Analisar o passo a passo em planilha | `historico.csv` |
| Comparar custos, melhorias e tempos das execuções | `resumo.csv`, na pasta da rodada |
| Consultar alocações e distribuição dos candidatos | `solucao.json` |
| Consultar média, melhor resultado, desvio padrão e falhas | `resumo.json`, na pasta da rodada |

No HTML, filtre por **Construção** ou **Busca local** e pela tentativa. A tabela mostra 50 eventos por página. No CSV do histórico, uma troca indica o primeiro candidato indo da origem para o destino e o segundo fazendo o caminho inverso. No Excel, importe `historico.csv` usando ponto e vírgula como delimitador e ponto como separador decimal; `resumo.csv` usa vírgula como delimitador e ponto como separador decimal.

Os arquivos gerados são resultados de experimentos, não entradas do algoritmo. Você pode apagar rodadas antigas para limpar os resultados; mantenha os arquivos de `instancias/` para executar novos experimentos. Resultados estão ignorados pelo Git conforme `.gitignore`.

Código de saída: `0` para sucesso, `1` para entrada/configuração inválida e `2` quando alguma execução não encontra solução. Resultados são aproximações heurísticas e não incluem certificado de otimalidade.

## Catálogo de instâncias

```powershell
dotnet run -c Release -- --listar-instancias
dotnet run -c Release -- --instancia instancia_2_g03 --max-tipos 2 --execucoes 10
```

Todas as 28 instâncias únicas fornecidas estão em `instancias/`, incluindo as duas extraídas do ZIP do grupo 2. O padrão é `instancia_<numero>_g<grupo com dois dígitos>.txt`. Exemplo: `instancia_1_g08.txt`. O conteúdo dos dados foi preservado; somente nomes e terminações de linha foram normalizados. `instancias/catalogo.csv` associa cada nome de origem ao nome padronizado e documenta duplicatas, arquivos sem dados e hash do conteúdo normalizado. Duas duplicatas são representadas por uma única cópia; dois arquivos que continham apenas um caminho externo não são entradas executáveis.

O algoritmo não carrega nenhuma instância automaticamente. Use `--listar-instancias` para consultar os nomes ou um caminho explícito para dados externos. As instâncias são entradas para os experimentos; nenhuma regra da heurística depende dos nomes padronizados. Os nomes são usados apenas para localizar arquivos e organizar os resultados.

## Codificação e terminações de linha

Os arquivos do projeto e os resultados gerados usam **UTF-8** e terminações **Windows (CRLF)**. `.editorconfig` configura o editor, `.gitattributes` orienta o Git e a rotina de exportação normaliza as saídas CSV, JSON, HTML e SVG. Preserve esse padrão ao editar os arquivos.

## Arquivos principais

- `Modelos.cs`: entrada genérica, solução, contadores e validação.
- `LeitorTxt.cs`: interpretação do formato TXT da disciplina.
- `Heuristica.cs`: construções e busca local.
- `Program.cs`: parâmetros, execuções e exportação.
- `Catalogo.cs`: resolução por nome e localização das pastas.
- `Historico.cs`: registro cronológico das ações.
- `Apresentacao.cs`: tabela e gráfico exportáveis.

O código atende à parte de implementação. Relatório em LaTeX/SBC, PDF, slides e publicação no GitHub são entregas separadas, ainda não produzidas por este projeto.
