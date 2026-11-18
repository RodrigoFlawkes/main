# Wave Studio — próxima etapa

ID: F2-I6B1-PROOF-4 — prova silenciosa com validação temporal por janelas
Base: f72304ebd6061a008c5b4e04a526fb7c0efa19a2; 737 testes por configuração, 214 direcionados, 2 Explicit excluídos. Revisão estática em REVIEW.md. Esforço Alto. A prova 3 continua não aprovada.

## Prova física única

Sincronizar fast-forward seguro, ler AGENTS, REVIEW, PROJECT_STATUS, WASAPI_BACKEND_PLAN e ALPHA_ACCEPTANCE. Conferir main limpa e binário Release x64 correspondente à implementação. Os sete manuais permanecem aprovados; não repetir sem alteração relevante. Exigir nova autorização explícita de captura de 5s exclusivamente SILENT, headset conectado, Wave Studio fechado normalmente, ausência de chamadas e outros testes/runners. Confirmar nomes exatos únicos e ativos: Microphone (fifine Microphone) e Alto-falantes (USB Audio Device), sem fallback.

Executar somente WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport com --explicit only --filter-method e nome completo, runner .NET direto Release. Variáveis opt-in WAVESTUDIO_SILENT_START_AUTHORIZED, WAVESTUDIO_SILENT_PREREQUISITES_CONFIRMED (=1), WAVESTUDIO_SILENT_CAPTURE_NAME e WAVESTUDIO_SILENT_HEADSET_NAME somente no processo autorizado. Sem repetir builds/regressões atualizados, sem alteração de código, parâmetros, tolerâncias, sistema, drivers, política Restricted ou volumes. Não encerrar à força; preservar guard se cleanup incerto. Captura em memória, sem armazenamento de voz, render sempre SILENT; não repetir automaticamente.

## Critérios e evidências

TransportObserved=true, janela completa de 5s, Start/Stop/release confirmados, nenhum recurso retido/buffer em voo, >=2 avanços válidos por papel, produção/leitura real FIFO, contabilidade consistente. Exigir adicionalmente Capture.CaptureTiming não nulo, Confirmed=true, ContinuousPackets=PacketsAccepted, ConfirmedClockWindows e ConfirmedPacketWindows positivos, janelas finais 2–2,5s e acumulados dentro da tolerância nominal, corroboração terminal dentro do limite. Render estrito, CaptureTiming null nele. Não confundir leitura provisória com drift comprovado; registrar margem real, contadores, âncoras, resíduos, janelas, erro/limite de corroboração, motivo de falha e históricos dos dois clocks. Descarte inicial opcional no máximo um pacote. Reportar formatos, períodos, capacidade, padding, HRESULTs, pacotes/frames, drops/underruns, tempo, cleanup; sem alegação de latência fim a fim. Se falhar, preservar JSON e causa/última evidência, parar para revisão. Sem repetir prova ou ampliar margem. Se passar, registrar confirmação real sem inferir causa da prova2/3.

Atualizar PROJECT_STATUS, CHANGELOG, WASAPI_BACKEND_PLAN, ALPHA_ACCEPTANCE e README (conciso), commit/push documental conforme autorização. JSON original local sem IDs/nomes/PCM. Não alterar NEXT_TASK/REVIEW. Não avançar B2/F3. Não instalar plugins ou excluir backups.

## Checkpoints seguintes

Após prova4 e revisão, executar tarefa separada COMENTARIOS-PTBR: inventariar/traduzir todos os comentários explicativos próprios em src/tests/scripts/configs versionados, inclusive XML, preservando código, diretivas, licenças, terceiros e gerados; verificar equivalência de tokens/sintaxe/strings e builds/testes; commit exclusivamente textual. Não misturar essa tradução com mudança funcional. Comentários novos em pt-BR desde já. Internacionalização Config → Idioma obrigatória na primeira UI pós-F3, recursos por cultura, pt-BR padrão, seguir Windows, persistência e expansão de traduções reais conforme REQUIREMENTS. Não implementar UI durante prova4. B2/F3 dependem de aprovação física/revisão; falhas de alocação intermitentes continuam registradas e não podem ter asserções enfraquecidas.
