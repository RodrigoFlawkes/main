# Aceitação da prévia v0.1.0 Alpha

Estado da F1-I6 em 12/11/2026 — validação pendente. Este roteiro registra evidências do proprietário e verificações técnicas. A aceitação da Fase 1 depende dos ensaios físicos e de aprovação explícita.

O executável local está em `artifacts/alpha-preview/win-x64/WaveStudio.exe`. Use a pasta completa da publicação. Requer .NET Desktop Runtime 10 x64, com Microsoft.NETCore.App e Microsoft.WindowsDesktop.App da linha 10. O ensaio utilizou 10.0.15 em Windows 10 Pro 22H2, build 19045.6466, experimental. Windows 11 x64 é prioritário e aguarda ensaio próprio.

Na raiz do repositório, inicie com armazenamento isolado para aceitação.

```powershell
$arquivoDeAceitacao = Join-Path $env:TEMP ('WaveStudio-aceitacao-' + [guid]::NewGuid().ToString('N') + '/settings.json')
& './artifacts/alpha-preview/win-x64/WaveStudio.exe' --settings-file $arquivoDeAceitacao
```

Mantenha `$arquivoDeAceitacao` para repetir o teste de reinício. A janela permite ver o caminho na aba Preferências locais.

Antes dos testes, use volume baixo, anote volume/mute/balanço de endpoints e aplicativos e padrões multimídia/comunicações. Restaure esses valores ao terminar. A reprodução, fala, conexão USB e suspensão são ações do proprietário. Para ensaios de arquivos, use --settings-file em uma pasta temporária.

| Caso | Ação e evidência esperada | Estado |
| --- | --- | --- |
| A01 Abrir e selecionar | Abrir WaveStudio.exe, comparar nomes/padrões com Windows e selecionar entrada/saída independentemente | Pendente |
| A02 Picos | Reproduzir áudio e falar por ação própria. Conferir movimento nos picos e silêncio, com monitoramento desligado | Pendente |
| A03 Endpoint | Em nível baixo, testar volume/mute, balanço L/R e centro, mouse/teclado e mudanças externas. Captura mono mostra balanço indisponível | Pendente |
| A04 Mixer | Abrir dois aplicativos reproduzindo, conferir sessões distintas, picos amostrados a cada 1 s e isolamento real de volume/mute | Pendente |
| A05 Recuperação | Remover/reconectar USB, conferir preferência explícita e seguir padrão por direção, alterar padrão no Windows e suspender/retomar por ação própria | Pendente |
| A06 Preferências | Salvar níveis, fechar/reabrir, conferir seleção e valores salvos. Recusar restauração e comparar níveis. Confirmar somente após conferir alvo e valores baixos | Pendente |
| A07 Arquivos | Em pasta temporária, conferir backup, corrupção, recuperação explícita e arquivo futuro preservado | Pendente |
| A08 Interface | Conferir rolagem, foco, diálogos e teclado em 1366×768 ou resolução disponível e DPI 100/150/200% | Pendente |
| A09 Estabilidade | Observar uso prolongado, atualizar/trocar seleção e fechar durante atividade. Registrar travamento, erro ou recuperação | Pendente |
| A10 Windows 11 | Repetir os casos no Windows 11 x64 e registrar versão/dispositivo | Pendente — ambiente a confirmar |

Envie o sistema/versão, dispositivo e resultados por ID, usando aprovado, falhou ou pendente. Em falha, informe ação, mensagem e valores antes/depois. Resultados observados por acessibilidade e testes automatizados ficam registrados separadamente em PROJECT_STATUS.md e TEST_PLAN.md.

## Evidências técnicas da F1-I6

| Verificação | Resultado observado | Estado |
| --- | --- | --- |
| Restore locked | Restauração e auditoria aprovadas após repetir com acesso ao NuGet | Aprovado |
| Debug x64 | Build com zero erros/avisos e 184 testes aprovados, incluindo os 174 anteriores | Aprovado |
| Release x64 | Build com zero erros/avisos e 184 testes aprovados | Aprovado |
| Medidores de sessão | Quatro sessões reais expuseram pico zero. Consulta completa com picos teve média de 1,68 ms em 100 leituras. UI usa 1 Hz, com pausa/atraso identificados | Aprovado em leitura — áudio ativo pendente |
| Publicação | Apphost PE AMD64 0x8664, dependente de framework, executável iniciado com arquivo temporário próprio | Aprovado |
| Processo prolongado | 600,34 s, 61 amostras, sem saída prematura. Encerramento normal confirmado por WaitForExit | Aprovado no ensaio do processo |
| Recursos | CPU de 1.125 ms no intervalo, média equivalente a 0,187% de um núcleo incluindo inicialização. Após 60 s, média de 0,0087% de um núcleo, working set 63,12–72,24 MiB e handles 362–371, terminando em 362 | Observado nesta máquina |
| Eventos de erro | Consulta de Application Error/.NET Runtime no intervalo encontrou zero eventos correspondentes ao WaveStudio.exe | Aprovado nessa consulta |
| Automação visual | Pipe nativo indisponível, inclusive após recuperação. Superfícies de UI indisponíveis | Bloqueado — inspeção humana pendente |
| Áudio físico e Windows 11 | Aguardam relato do proprietário e ambiente apropriado | Pendente |

Picos zero e custo de consulta descrevem as leituras observadas. A aceitação dos picos com fala/música e da interação física pertence aos casos A01–A10. O campo Responding da API de processo foi coletado, com inspeção visual indisponível. O ensaio de recursos cobre esta execução e duração.

## Reprodução da publicação local

Em Windows x64 com o SDK fixado, execute os comandos abaixo. O projeto define PlatformTarget x64, e o apphost desta máquina foi conferido como AMD64.

```powershell
dotnet restore WaveStudio.slnx --locked-mode
dotnet build WaveStudio.slnx -c Release --no-restore
dotnet publish src/WaveStudio.Desktop/WaveStudio.Desktop.csproj -c Release --no-restore --self-contained false -p:DebugType=None -p:DebugSymbols=false -p:IncludeSourceRevisionInInformationalVersion=false -o artifacts/alpha-preview/win-x64
```

A pasta de execução contém WaveStudio.exe, WaveStudio.dll, WaveStudio.Audio.dll, NAudio.Core.dll, NAudio.Wasapi.dll, System.Numerics.Tensors.dll e os dois JSON de execução. Os avisos de terceiros e este roteiro acompanham a pasta. As licenças são copiadas de docs/licenses e do pacote System.Numerics.Tensors 9.0.0 instalado pelo restore. [Inventário de componentes](THIRD_PARTY_NOTICES.md).

Após publicar, copie os avisos para acompanhar a prévia.

```powershell
$destino = Join-Path $PWD 'artifacts/alpha-preview/win-x64'
$avisos = Join-Path $destino 'licenses'
New-Item -ItemType Directory -Path $avisos -Force | Out-Null
Copy-Item -LiteralPath './docs/licenses/NAudio-MIT.txt', './docs/licenses/DotNet-MIT.txt' -Destination $avisos
$cachePacotes = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$tensors = Join-Path $cachePacotes 'system.numerics.tensors/9.0.0'
Copy-Item -LiteralPath (Join-Path $tensors 'LICENSE.TXT') -Destination (Join-Path $avisos 'System.Numerics.Tensors-LICENSE.TXT')
Copy-Item -LiteralPath (Join-Path $tensors 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $avisos 'System.Numerics.Tensors-THIRD-PARTY-NOTICES.TXT')
Copy-Item -LiteralPath './docs/ALPHA_ACCEPTANCE.md', './docs/THIRD_PARTY_NOTICES.md' -Destination $destino
```

SHA-256 do WaveStudio.exe local, com 162.304 bytes.

```text
7D4749DB85606830D7851BBBDE70181530BAA5A20592070B37FC5FAF94470F92
```

Relatório detalhado do ensaio fica em artifacts/alpha-preview/validation, ignorado pelo Git. A pasta publicada é uma prévia para testes locais. A aprovação final da Alpha aguarda as evidências físicas e aprovação do proprietário.
