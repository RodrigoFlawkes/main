# Aceitação da prévia v0.1.0 Alpha

Estado da F1-I6-MIC em 12/11/2026 — validação física pendente. Este roteiro registra evidências do proprietário e verificações técnicas. A aceitação da Fase 1 depende dos ensaios físicos e de aprovação explícita.

O executável atualizado F1-I6-MIC está em `artifacts/alpha-preview/mic-build/WaveStudio.exe`. A pasta habitual `artifacts/alpha-preview/win-x64` aguarda o fechamento normal da prévia anterior pelo proprietário antes de ser substituída; não foi encerrado seu processo. Use a pasta completa da publicação atualizada. Requer .NET Desktop Runtime 10 x64, com Microsoft.NETCore.App e Microsoft.WindowsDesktop.App da linha 10. O ensaio utilizou 10.0.15 em Windows 10 Pro 22H2, build 19045.6466, experimental. Windows 11 x64 é prioritário e aguarda ensaio próprio.

Na raiz do repositório, inicie com armazenamento isolado para aceitação.

```powershell
$arquivoDeAceitacao = Join-Path $env:TEMP ('WaveStudio-aceitacao-' + [guid]::NewGuid().ToString('N') + '/settings.json')
& './artifacts/alpha-preview/mic-build/WaveStudio.exe' --settings-file $arquivoDeAceitacao
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
| A10 Windows 11 | Repetir os casos no Windows 11 x64 e registrar versão/dispositivo | Pendente — proprietário dispõe somente de Windows 10 |
| A11 Testar microfone | Com fones e volume baixo: gravar, falar, parar, ouvir explicitamente na saída selecionada, parar reprodução e limpar. Conferir limite 10 s, mono, nível digital, ausência de captura/playback automático e descarte em mudança/USB/suspensão/fechamento | Pendente — nenhuma captura/reprodução física automatizada |

Envie o sistema/versão, dispositivo e resultados por ID, usando aprovado, falhou ou pendente. Em falha, informe ação, mensagem e valores antes/depois. Resultados observados por acessibilidade e testes automatizados ficam registrados separadamente em PROJECT_STATUS.md e TEST_PLAN.md.

O proprietário confirmou disponibilidade para testar após o executável ficar pronto. A prévia já foi disponibilizada. A primeira rodada foi iniciada no Windows 10. Evidências parciais do proprietário abaixo; casos completos ainda aguardam confirmação.

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

Em Windows x64 com o SDK fixado, feche normalmente o executável da pasta de destino antes de publicar. Execute os comandos abaixo. O projeto define PlatformTarget x64, e o apphost desta máquina foi conferido como AMD64. Nesta etapa foi usada a pasta de preparação mic-build para preservar a janela anterior ainda aberta. Os comandos abaixo reproduzem a pasta habitual após seu fechamento.

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

## Novo teste A11 e evidências técnicas F1-I6-MIC

1. Selecione o microfone e o headset na aba Dispositivos. Confira IDs/nomes, mute e níveis baixos do Windows. O teste usa exatamente esses alvos, sem escolher outro se ficarem indisponíveis. Para microfone mono, balanço L/R continua indisponível.
2. Abra Testar microfone e confirme que nada está sendo capturado/reproduzido. O nível digital para ouvir começa em 20%, separado do Windows. Use fones.
3. Clique em Gravar teste, fale uma frase curta, confira GRAVANDO e o tempo. Use Parar; em outra tentativa aguarde o limite de 10 s. Parar também está disponível na reprodução. Nunca deve ouvir automaticamente ao terminar.
4. Clique em Ouvir gravação, confirme voz e saída selecionada. Confira nível digital com valores baixos; não aumente níveis do sistema para compensar uma falha sem investigar mute/permissões. Use Parar reprodução e repita explicitamente se necessário.
5. Clique em Limpar teste. Ouvir deve ficar desabilitado. Troque um dispositivo, atualize a lista ou feche e confirme que o trecho não retorna. O teste não cria arquivos de áudio nem salva seu nível digital.
6. Teste mute/permissões e formatos diferentes quando disponíveis. Com hardware seguro, troque/desconecte USB, suspenda/retome ou feche durante gravação/reprodução. A operação deve parar, o trecho ser descartado e nunca reiniciar automaticamente ou migrar para outro endpoint.
7. Relate A11 aprovado/falhou/pendente, sistema, entrada/saída, canais/formato, ação e mensagem. Não envie áudio pessoal; uma descrição do resultado basta. Restaure os níveis/padrões originais ao terminar.

Restore locked com auditoria aprovado; Debug/Release sem erros/avisos, 228 testes aprovados em cada configuração (44 novos + regressão dos 184). Todos os testes novos de captura/playback são simulados. O novo pacote abriu com título esperado, fechou normalmente e criou somente configuração/lock isolados. Nenhum teste automatizado abriu o microfone real ou emitiu áudio de teste. A negociação de formatos e a qualidade audível continuam pendentes de A11. Monitoramento ao vivo e motor completo da Fase 2 não foram iniciados.

O apphost WaveStudio.exe tem o mesmo SHA-256 indicado acima porque o destino WaveStudio.dll é constante. Não copie somente o EXE. O WaveStudio.dll atualizado possui 121.344 bytes e SHA-256 `2FDFA46222331D51D30BDB01AD0B512BF8D4DFF360DDA561A37FE3567C80D20E`; o manifesto local registra também a DLL de áudio e todos os arquivos da pasta completa.

## Evidências físicas parciais do proprietário — 12/11/2026

- Capturas de tela: prévia aberta, 67 endpoints, saída Alto-falantes (3- USB Audio Device), 48 kHz / 2 canais / contêiner 32 bits; entrada Microfone (3- USB Audio Device), 48 kHz / 1 canal / contêiner 32 bits. Ambos padrões multimídia; entrada também padrão de comunicações. Detecção automática ativa. A01: abertura e reconhecimento confirmados; comparação integral e seleção independente ainda precisam de relato explícito.
- A03: proprietário confirmou funcionamento de volume e mute do microfone após novo teste. Balanço do microfone indisponível, comportamento esperado para endpoint mono; não é defeito. Demais subcasos de A03 permanecem pendentes de evidência detalhada.
- A04/A06: abas Mixer (cinco sessões inativas, picos zero) e Preferências locais vistas nas imagens; não equivalem a isolamento audível ou persistência física completos. Proprietário relatou que somente volume/mute de microfone pareciam inicialmente falhar e depois confirmou ambos. Não inferir aprovação de todos os casos.
- A02 e demais casos: confirmar explicitamente reação dos medidores, isolamento, reconexão, restauração e restantes conforme roteiro. Windows 11 permanece pendente.
- Solicitação nova: teste de microfone dentro do programa para ouvir a própria voz. Adendo F1-I6-MIC prevê gravar/reproduzir trecho curto em memória para auxiliar a aceitação; monitoramento digital contínuo fica na Fase 2 com proteção contra feedback. Não confundir reprodução do teste com sidetone em tempo real.
