# Aceitação da prévia v0.1.0 Alpha

Atualização de identidade em 13/11/2026: logo oficial no cabeçalho, ícone incorporado e metadados oficiais. 252 testes aprovados por configuração, Debug/Release sem erros/avisos. Conferir visualmente cabeçalho proporcional, transparência, ícones de janela/taskbar/Alt+Tab em diferentes DPIs; atalhos futuros devem usar WaveStudio.exe, índice 0. Nenhum instalador ou release pública foi criado. Critérios físicos anteriores permanecem válidos.

Pacote de identidade atual: WaveStudio.exe com 303.616 bytes, SHA-256 `97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285`; WaveStudio.dll SHA-256 `E2463B1B7871894F94DDE9FB0FC1DD7E504484CBA23FD765CCF81537D70CD941`. Identificação dos pacotes anteriores abaixo é histórica. Manifesto completo em artifacts/alpha-preview/validation/branding-package-manifest.json. Use a pasta inteira; imagens externas não são necessárias.

Estado da F1-I6-METER em 13/11/2026 — validação física pendente. Este roteiro registra evidências do proprietário e verificações técnicas. A aceitação da Fase 1 depende dos ensaios físicos e de aprovação explícita.

O executável atualizado F1-I6-METER está em `artifacts/alpha-preview/win-x64/WaveStudio.exe`. O proprietário confirmou o fechamento normal da prévia anterior antes da republicação. Use a pasta completa da publicação atualizada. Requer .NET Desktop Runtime 10 x64, com Microsoft.NETCore.App e Microsoft.WindowsDesktop.App da linha 10. O ambiente de validação usa Windows 10, experimental. Windows 11 x64 é prioritário e aguarda ensaio próprio.

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
| A10 Windows 11 | Repetir os casos no Windows 11 x64 e registrar versão/dispositivo | Pendente — proprietário dispõe somente de Windows 10 |
| A11 Testar microfone | Com fones e volume baixo: gravar, falar, parar, ouvir explicitamente na saída selecionada, parar reprodução e limpar. Conferir limite 10 s, mono, nível digital, ausência de captura/playback automático e descarte em mudança/USB/suspensão/fechamento | Sucesso relatado na gravação/reprodução após ajustes próprios; subcasos completos pendentes |
| A12 Nível de entrada | Comparar a mesma entrada com o Windows: fala, silêncio/ruído, leitura numérica, zero/indisponibilidade/atraso, troca de seleção, mute, USB, minimização/retomada e leitura durante gravação explícita | Pendente — sem atribuição de causa ao problema anterior |

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

Em Windows x64 com o SDK fixado, feche normalmente o executável da pasta de destino antes de publicar. Execute os comandos abaixo. O projeto define PlatformTarget x64, e o apphost desta máquina foi conferido como AMD64. Os comandos reproduzem a pasta habitual, republicada na F1-I6-METER após o fechamento confirmado pelo proprietário.

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

SHA-256 histórico do apphost F1-I6-METER anterior à identidade visual, com 162.304 bytes.

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

O apphost WaveStudio.exe pode manter o mesmo SHA-256 porque o destino WaveStudio.dll é constante. Não copie somente o EXE. Na F1-I6-MIC, a DLL tinha 121.344 bytes e SHA-256 `2FDFA46222331D51D30BDB01AD0B512BF8D4DFF360DDA561A37FE3567C80D20E`. A identificação da implementação atual F1-I6-METER está abaixo.

## Evidências físicas parciais do proprietário — 12/11/2026

- Capturas de tela: prévia aberta, 67 endpoints, saída Alto-falantes (3- USB Audio Device), 48 kHz / 2 canais / contêiner 32 bits; entrada Microfone (3- USB Audio Device), 48 kHz / 1 canal / contêiner 32 bits. Ambos padrões multimídia; entrada também padrão de comunicações. Detecção automática ativa. A01: abertura e reconhecimento confirmados; comparação integral e seleção independente ainda precisam de relato explícito.
- A03: proprietário confirmou funcionamento de volume e mute do microfone após novo teste. Balanço do microfone indisponível, comportamento esperado para endpoint mono; não é defeito. Demais subcasos de A03 permanecem pendentes de evidência detalhada.
- A04/A06: abas Mixer (cinco sessões inativas, picos zero) e Preferências locais vistas nas imagens; não equivalem a isolamento audível ou persistência física completos. Proprietário relatou que somente volume/mute de microfone pareciam inicialmente falhar e depois confirmou ambos. Não inferir aprovação de todos os casos.
- A02 e demais casos: confirmar explicitamente reação dos medidores, isolamento, reconexão, restauração e restantes conforme roteiro. Windows 11 permanece pendente.
- Solicitação nova: teste de microfone dentro do programa para ouvir a própria voz. Adendo F1-I6-MIC prevê gravar/reproduzir trecho curto em memória para auxiliar a aceitação; monitoramento digital contínuo fica na Fase 2 com proteção contra feedback. Não confundir reprodução do teste com sidetone em tempo real.

## Adendo de diagnóstico — 13/11/2026

A11 permanece pendente: proprietário relatou zumbido sem voz tanto na prévia quanto no teste do Windows; relato posterior indicou reação da entrada no Windows, mas a troca de USB-C/C por USB-C/A não resolveu o problema informado. Não houve confirmação audível de gravação correta no Wave. Comparar mesmo dispositivo, níveis/mute e condições no Windows e Wave, sem diagnosticar causa apenas pela barra. Solicitação adicional: medidor de captação visível na aba Testar microfone, com leitura real e estado de indisponibilidade, sem gravar automaticamente. Novo teste após implementação: observar fala/silêncio, troca de entrada, mute, desconexão, ausência de captura automática e comportamento durante gravação explícita; registrar limitações do driver e qualidade da gravação separadamente.

## A12 — comparação do medidor na nova prévia F1-I6-METER

Complemento de evidência recebido em 13/11/2026, revisão ed19c6e: o proprietário informou que o teste de áudio voltou a funcionar após ajustes próprios não especificados. Registrado sucesso relatado da função principal de gravação/reprodução A11. O histórico de zumbido acima é preservado, sem atribuir causa a cabo, driver, configuração ou código. A11 integral continua pendente dos subcasos de descarte, limite, mute, troca/desconexão e recuperação; A12 ainda aguarda ensaio na nova prévia.

1. Selecione a mesma entrada no Wave e no Windows, anotando níveis/mute/padrões. Abra Testar microfone e localize Nível de entrada do microfone. A barra consulta o medidor existente; abrir a aba não inicia gravação.
2. Observe alguns segundos sem falar, depois fale uma frase e observe o percentual. Registre a reação e o comportamento no Windows nas mesmas condições. A amplitude não confirma que o sinal contém voz nem explica um zumbido.
3. Diferencie 0% com leitura disponível, mensagem de indisponibilidade e leitura atrasada. Se não houver reação fora de captura ativa, clique explicitamente em Gravar teste e compare durante a gravação. Registre essa limitação do dispositivo; não trate ausência de leitura como silêncio confirmado.
4. Confira que trocar a entrada limpa o valor anterior e que uma saída diferente não muda a preferência de entrada. Teste mute, USB, minimização e retomada, restaurando os valores originais. Mixer/Preferências pausam a medição; Dispositivos e Testar microfone reutilizam o mesmo worker.
5. Use A11 separadamente para gravar e ouvir com fones e nível digital baixo. Relate se há voz, ruído/zumbido ou ausência de áudio. Pare e limpe. Envie somente a descrição do resultado, sistema e dispositivos, sem áudio pessoal.

Restore locked com auditoria aprovado. Debug/Release x64 sem erros/avisos, 248 testes aprovados em cada configuração (228 anteriores + 20 novos). Testes novos verificam apresentação, geração/frescor, pausa/retomada, fechamento e ausência de captura automática, com áudio simulado. Reação do medidor a fala/silêncio/ruído, subcasos A11, layout/DPI e Windows 11 continuam pendentes de evidência física completa. A Alpha não foi declarada aceita e a Fase 2 não foi iniciada.

Pacote Release AMD64 conferido com 14 arquivos e avisos de terceiros. Executável iniciou e fechou normalmente na pasta habitual, usando configuração isolada e sem acionar gravação/reprodução. Esse ensaio comprova abertura/encerramento do processo, com aceitação física e inspeção visual completa ainda pendentes.

Identificação histórica do pacote F1-I6-METER anterior à identidade visual:

| Arquivo | Bytes | SHA-256 |
| --- | --- | --- |
| WaveStudio.dll | 124.928 | 068B5EBB063EC80EFBA2EF02F6C94A1EAF1100E25D7925BFB4D79EC41FEB3E76 |
| WaveStudio.Audio.dll | 100.864 | 68350948F73E0AB14F983DFDBD6BD10C03177CEA8580AC0BEC480346830C5F5F |

Conteúdo e hashes completos em artifacts/alpha-preview/validation/meter-package-manifest.json, ignorado pelo Git. Distribua/use a pasta completa com as licenças existentes.

## Conferência visual da prévia com identidade oficial

Base ace3e1e. Quatro capturas do proprietário confirmam logo no cabeçalho, ícone na barra de título e abertura das quatro abas. Catálogo com 51 endpoints; saída USB Audio Device estéreo 48 kHz e microfone fifine Microphone mono 48 kHz, ambos com contêiner 32 bits; preferência explícita de captura e seguimento do padrão de reprodução. Mixer mostra oito sessões, incluindo League of Legends e Discord ativas com picos não nulos. A aba Testar microfone mostra medidor disponível com leitura pontual de 1,0%, sem gravação iniciada. Preferências carregadas, seleção salva visível e zero preferências de níveis. Isso confirma apresentação e leituras pontuais, não reação à fala/silêncio, isolamento audível, hotplug ou restauração de níveis. Taskbar/Alt+Tab, DPI, demais subcasos de A01–A12 e Windows 11 ainda pendentes. Recomenda-se concluir checklist de aceitação no Windows 10 antes do redesign, sem reexecutar tarefa F1-I6-METER já entregue. Observação visual: detalhes técnicos ocupam espaço excessivo e controles inferiores exigem rolagem; priorizar compactação no futuro redesign sem ocultar controles funcionais.
