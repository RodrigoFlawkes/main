# Plano de testes

## F1-I4 — resultados atuais

Restore locked aprovado e build Debug x64 com zero erros/avisos. Suíte com 101 aprovados, zero falhas e zero ignorados, incluindo os 77 anteriores. São 95 casos de regras, simulações e grid WinForms STA e seis integrações Windows de leitura, usando TESTINGPLATFORM_TELEMETRY_OPTOUT=1.

As regressões cobrem pico zero/intermediário/máximo, NaN/infinito/fora da faixa, indisponibilidade, coalescência de duplicatas/rajadas com prazo máximo, reutilização de fontes, troca e revisão de seleção, ausência/retorno, recuperação após falha, inscrição indisponível e fallback, pausa de medição oculta, descarte idempotente e callback tardio. Worker bloqueado em leitura aguarda a operação e descarta fontes na mesma thread. Preferências testam padrão por direção, seleção explícita ausente e retorno ao modo automático. Grid STA testa identidade de linha/célula, edição/cancelamento, binding e snapshot atrasado na reescolha do mesmo endpoint. Sessão inacessível na abertura ou leitura da chave antes do alvo válido tem regressão própria, preservando os testes de erro do alvo e identidade antes de escrever.

Integração de leitura abriu medidores reais e inscrição de notificações. Foram observados 20 snapshots em 2,21 s, picos de reprodução/captura em zero, detecção automática ativa e consulta média de 0,083 ms. Delta de CPU do runner completo de 31,2 ms, working set inicial/final de 59.949.056/61.140.992 bytes. Cinco sessões expuseram medidor, todas com pico zero na leitura pontual. A amostra mede o ambiente local e inclui o runner. Escritas automatizadas continuam em simulações.

O executável abriu e encerrou normalmente. Acessibilidade reconheceu barras reais, textos de silêncio, opções seguir padrão e detecção automática ativa. Captura visual retornou window capture timed out nas duas tentativas. Os testes STA verificam comportamento da grid. Validação visual da aplicação permanece pendente.

### Aceitação física da F1-I4

1. Anotar volume, mute, balanço e padrões multimídia/comunicações antes dos ensaios e restaurar ao final. Comparar abertura e atualização com os níveis existentes no Windows.
2. Reproduzir áudio em nível confortável e falar no microfone por ação do proprietário. Conferir resposta independente dos picos e retorno ao silêncio. Comparar dispositivo selecionado e origem do sinal. Conferir indisponibilidade em endpoint inativo.
3. Selecionar explicitamente USB, removê-lo e reconectá-lo. Conferir preferência preservada, controles/mixer desabilitados durante ausência e recuperação do mesmo ID. Repetir trocas rápidas durante leitura e edição, inclusive reescolha do mesmo ID.
4. Em modo seguir padrão, alterar padrão multimídia de reprodução e captura separadamente pelo Windows. Conferir somente a direção correspondente. Alterar padrão de comunicações e conferir marcas reais. Repetir com preferência explícita e observar sua preservação.
5. Por ação do proprietário, suspender e retomar a máquina. Conferir nova inscrição/consulta, barras, mixer, níveis existentes e encerramento estável. Repetir USB e atualização manual após retomada.
6. Abrir dois aplicativos reproduzindo, conferir volume/mute por instância, mudanças externas, foco e rolagem durante polling, edição numérica, Enter/Esc, criação/encerramento e sessões inacessíveis. Restaurar valores anteriores. Repetir os passos físicos dos Incrementos 2 e 3.
7. Alternar abas/minimizar e conferir retomada dos medidores, ausência de flicker e edição estável. Verificar consumo e estabilidade por período prolongado, com diversos dispositivos/sessões. Fechar durante eventos/operações.
8. Validar rolagem e controles em 1366×768 e DPI 100/150/200%. Executar em Windows 11 x64. Windows 10 continua experimental.

Medidores contínuos por sessão aguardam avaliação de ciclo de vida e custo. A integração atual valida leitura pontual dessa API. Os cenários físicos acima permanecem pendentes.

## Histórico F1-I3 — resultados e aceitação pendente

Restore locked e build Debug x64 aprovados, com zero erros/avisos. Suíte final com 77 aprovados, zero falhas e zero ignorados, incluindo os 56 anteriores. São 73 casos de simulações/regras e 4 integrações Windows de leitura. Telemetria MTP desativada na sessão.

Novos testes verificam volume 0/100% e frações, valores inválidos, mute/unmute, abertura e refresh sem escrita, mudanças externas, instâncias do mesmo PID, sons do sistema, identidade alterada antes da escrita, PID reutilizado, endpoint divergente, sessão encerrada/removida, novas sessões, acesso negado, falhas parciais de leitura, descarte e troca de seleção com comando enfileirado. Regressões da revisão cobrem 2.000 seleções, descarte durante operação e balanço com alteração externa detectável.

Integração de leitura no endpoint padrão encontrou quatro sessões inativas, zero avisos, incluindo duas instâncias do mesmo PID e sons do sistema. Em dez leituras completas foram observadas médias de 1,40 ms no endpoint e 1,57 ms no mixer. Valores são amostras de tempo de parede desta máquina. Ausência de sessões continua sendo resultado válido.

O executável abriu e as abas foram reconhecidas por acessibilidade. Captura visual continuou retornando timeout. A navegação automatizada não confirmou visualmente a lista do mixer. Layout e operações físicas permanecem pendentes.

Para aceitação física, o proprietário deve abrir dois aplicativos com reprodução no mesmo endpoint e anotar/restaurar os níveis originais. Conferir nome, volume numérico com Enter/Esc, mute/unmute e isolamento entre sessões e endpoints. Alterar volume pelo Windows e conferir atualização, iniciar/encerrar aplicativos, trocar rapidamente de saída, remover USB e encerrar durante operação. Conferir arraste contínuo, roda do mouse e teclado dos sliders de endpoint e rolagem em 1366×768 e DPI 100/150/200%. Validar Windows 11 x64. Windows 10 segue experimental. As escritas dos testes automatizados usam simulações.

As seções seguintes preservam verificações e critérios dos incrementos anteriores e o planejamento das fases posteriores.

## Verificação do Incremento 2

O Incremento 1 foi publicado no commit 60238ea. O Incremento 2 recebeu aprovação provisória para publicação e revisão técnica. A suíte inclui a regressão dos 16 testes anteriores. Os testes de escrita usam endpoints simulados, preservando os níveis físicos do host. A nova integração Windows faz leitura dos controles reais e descarte dos wrappers/callbacks.

Verificação final — `dotnet restore WaveStudio.slnx --locked-mode` aprovado, `dotnet build WaveStudio.slnx -c Debug --no-restore` com zero erros/avisos e `dotnet test --solution WaveStudio.slnx -c Debug --no-build` com 56 aprovados, zero falhas e zero ignorados. São 53 casos de regras/simulações e 3 integrações Windows de leitura. A execução usou `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`.

Casos automatizados novos cobrem 0/100% e frações, rejeição de limites/NaN/infinito, mute/unmute, centro/extremos/intermediários, repetição sem acumular atenuação, silêncio, mono, máscara ausente e multicanal, suporte parcial, driver ignorando escrita, canais acoplados, restauração após falha e cancelamento, desconexão durante balanço, alterações externas simuladas, mudança de seleção, geração antiga para o mesmo ID, comandos enfileirados cancelados, isolamento de entrada/saída e descarte.

A integração real leu volume 100% e mute desligado dos endpoints padrão do host, com L/R 1/1 na reprodução e balanço desabilitado no microfone mono. A janela abriu e esses controles foram confirmados por acessibilidade. Captura de tela falhou por timeout. O layout e as operações físicas de escrita continuam pendentes.

## Passos físicos do Incremento 2

Antes de alterar níveis, anotar volume master, mute e níveis de cada canal dos endpoints de entrada/saída. Usar volume confortável e sinal de teste de baixa intensidade. Restaurar os valores anotados ao terminar, incluindo balanço e mute. Esses ensaios exigem ação explícita do usuário e hardware disponível.

1. Abrir o app e comparar os valores com os controles do Windows, confirmando que a abertura e a seleção mantêm níveis e padrões existentes.
2. Ajustar volume de reprodução pelo slider e pelo número. Conferir o valor no Windows. Repetir para captura quando disponível. Testar 0/100% em condições controladas, sem sinal alto ou monitoramento que possa causar feedback.
3. Ativar/desativar mute em cada direção e conferir isolamento, valor anterior de volume e estado no Windows.
4. Em estéreo com L/R confirmado, testar centro, esquerda, direita e posições intermediárias com sinal de canais conhecido. Conferir que o maior nível de canal preserva a base e que centralizar recupera ambos nessa base.
5. Selecionar microfone mono e endpoint sem mapeamento L/R. Conferir balanço desabilitado com motivo e demais controles funcionando conforme suporte. Caso um driver acople canais, conferir erro e tentativa de restauração.
6. Alterar volume/mute e canais pelo Windows. Aguardar aproximadamente um segundo e conferir atualização na UI sem oscilação ou reaplicação de valores anteriores.
7. Trocar rapidamente de dispositivo durante uma leitura/edição e conferir que a nova seleção mostra seus próprios níveis e recebe somente ações realizadas para ela.
8. Remover um USB durante ajuste. Conferir erro, controles desabilitados e atualização de lista. Reconectar e tentar novamente. Conferir e restaurar canais no Windows se uma operação de balanço tiver sido interrompida.
9. Fechar durante consulta ou comando, reabrir e conferir estabilidade e níveis existentes. Avaliar escala 100/150/200% e repetir em Windows 11 x64 suportado.

Registrar resultados por dispositivo/driver. As chamadas de balanço são sequenciais, e desconexão física pode impedir a restauração automática. O comportamento de modo exclusivo e o suporte de escrita por driver permanecem sujeitos a validação física.

## Resultados do Incremento 1

- `dotnet restore WaveStudio.slnx` passou após repetir com acesso autorizado ao NuGet fora do sandbox. `dotnet restore WaveStudio.slnx --locked-mode` também passou.
- `dotnet build WaveStudio.slnx -c Debug --no-restore` passou com zero erros e zero avisos.
- `dotnet test --solution WaveStudio.slnx -c Debug --no-build --output Detailed`, com telemetria MTP desativada na sessão, aprovou 16 testes, zero falhas e zero ignorados. Foram 14 casos de regras/mensagens e 2 testes de integração Core Audio.
- A integração enumerou 67 endpoints reais, com zero avisos. Observou estados ativo/ausente/desconectado, padrão de reprodução multimídia diferente do de comunicações, captura padrão e formatos de mixagem. Consultas concorrentes serializadas e consulta após cancelamento também passaram.
- `WaveStudio.exe` foi iniciado. A acessibilidade da janela confirmou nome/versão, seletores, dados de entrada e saída, botão Atualizar e status de 67 endpoints. A captura de tela falhou com `FrameArrived timed out` e `window capture timed out`. O clique automatizado falhou com `coordinate input geometry is unavailable`. A conferência visual e das interações permanece pendente.

Os testes `WindowsIntegration` consultam a configuração real do host. Um inventário vazio é válido. Avisos de falha de API reprovam esses testes e exigem diagnóstico local. Para executar apenas regras, use `dotnet test --solution WaveStudio.slnx --filter-not-trait Category=WindowsIntegration`.

## Passos físicos do Incremento 1

1. Executar `dotnet run --project src/WaveStudio.Desktop`. Conferir título, versão, textos, legibilidade e navegação por teclado em 100%, 150% e 200% de escala.
2. Comparar nomes, estados e padrões de multimídia/comunicações das duas listas com as configurações de som do Windows. Dispositivos históricos podem aparecer como ausentes. Ativos aparecem primeiro.
3. Selecionar outro headset e conferir nome, ID, estado, canais, taxa e bits do mix. Confirmar que a seleção de microfone e o padrão do Windows continuam os mesmos. Repetir trocando somente o microfone.
4. Clicar em Atualizar e conferir preservação das duas seleções. Alterar um padrão explicitamente pelo Windows e atualizar para conferir sua identificação.
5. Remover um USB selecionado e clicar em Atualizar. Conferir estado ausente/desconectado ou aviso de indisponibilidade. Reconectar, atualizar e conferir recuperação pelo mesmo ID quando preservado pelo Windows.
6. Consultar um endpoint inativo. Conferir indicação de formato indisponível. Fechar a janela durante uma atualização e reabrir para conferir estabilidade.

Registrar modelo/conexão, versão do Windows e resultado por passo. Repetir em Windows 11 x64 suportado. No Windows 10 local, os resultados atuais cobrem execução experimental e consultas reais. Os passos de volume, sessões e medidores abaixo pertencem aos incrementos seguintes.

## Testes automatizados planejados

| Área | Casos mínimos |
| --- | --- |
| Configurações | Round-trip JSON, padrão, migração conhecida, versão futura, corrupção, backup, arquivo ausente, falha de escrita e gravações concorrentes serializadas |
| Volume e balanço | Limites, centro neutro, extremos com atenuação, preservação do nível-base, mono, multicanal sem L/R e suporte ausente |
| Estado de dispositivos | Preferência ausente, seguir padrão, troca de seleção, eventos repetidos, remoção durante comando e descarte idempotente |
| Sessões | Criação/encerramento, PID reutilizado, sons do sistema, múltiplas sessões, identidade indisponível e isolamento das alterações |
| Apresentação | Medidor indisponível, atualização externa sem realimentação de comandos, resposta atrasada de seleção anterior e erro recuperável |

Testes com serviços simulados verificam regras e estados. Testes de integração Windows exercitam Core Audio, com seus resultados identificados separadamente.

## Aceitação física da Alpha

Preparar volume confortável, anotar valores iniciais do Windows e aplicativos e restaurá-los ao final. Usar fones quando necessário. Registrar sistema, versão do app, dispositivo/conexão, suporte exposto, passos, resultado e evidência por caso.

| Caso | Procedimento e resultado esperado |
| --- | --- |
| T01 Enumeração | Comparar entrada/saída, IDs, estados e padrões de multimídia/comunicações com o Windows. Propriedades ausentes são informadas |
| T02 Seleção | Selecionar entrada e saída separadamente. Somente o alvo administrado muda, sem alterar padrão do sistema |
| T03 Volume/mute | Alterar no app e conferir no Windows. Alterar no Windows e conferir atualização do app. Exercitar entrada e saída |
| T04 Balanço | Usar sinal estéreo conhecido e verificar centro/esquerda/direita. Mono ou suporte ausente desabilita o controle com explicação |
| T05 Sessões | Abrir dois aplicativos reproduzindo áudio. Ajustar volume/mute de uma sessão, conferir isolamento, encerrar e reabrir o aplicativo |
| T06 Medidores | Reproduzir áudio e falar no microfone. Picos reais reagem quando disponíveis, silêncio retorna a zero, indisponibilidade é explícita |
| T07 Hotplug | Remover/reconectar USB durante leitura e mudança de volume. UI permanece responsiva e reconcilia estado sem trocar preferências silenciosamente |
| T08 Padrão/retomada | Alterar padrão no Windows, suspender/retomar e testar ausência de endpoints. Reconexão recupera serviços e controles válidos |
| T09 Persistência | Reiniciar, conferir preferências e restauração explícita. Testar JSON inválido/futuro e pasta sem escrita, preservando dados recuperáveis |
| T10 Robustez/UI | Abrir/fechar repetidamente, trocar endpoints rapidamente, usar teclado e escala 100/150/200%. Verificar erros, layout e consumo observado |
| T11 Privacidade | Conferir recursos apresentados, ausência de arquivos de áudio criados automaticamente e logs sem conteúdo de áudio |

Executar no Windows 11 x64 suportado. Registrar separadamente resultados no Windows 10 local e sua elegibilidade de suporte. Cobertura por dispositivo deve incluir USB, analógico e Bluetooth disponíveis. HDMI/DP, virtual e hardware adicional permanecem pendentes até ensaio próprio. Anotar captura bloqueada por privacidade do Windows, modo exclusivo e controles ausentes.

## Prova externa das Fases 2–3

1. Documentar integração virtual candidata, versão, licença, assinatura, instalação autorizada, restauração e desinstalação. Manter proteções do Windows habilitadas.
2. Direcionar navegador à entrada virtual, processar ganho conhecido no motor e reproduzir no endpoint físico. Comparar bypass/processado com sinal conhecido e verificar ausência de reprodução duplicada.
3. Capturar microfone físico, aplicar processamento mensurável e entregar ao endpoint de captura virtual selecionado em OBS e Discord. Confirmar sinal recebido no aplicativo externo.
4. Medir latência ponta a ponta com método descrito, além de CPU/RAM, taxa, buffers, underruns/overruns e recuperação após hotplug. Separar medição de estimativa.
5. Testar loops/mix-minus, destinos externos opt-in e restauração após encerramento normal e inesperado. Desinstalar a integração de teste quando autorizado e conferir estado anterior.

Registrar aprovação ou bloqueio de cada direção antes de ampliar o catálogo de efeitos. Testes DSP posteriores usarão senos, impulsos, silêncio, ruído, clipping, taxas e canais distintos, com comparação objetiva de ganho/resposta, estabilidade e latência.
