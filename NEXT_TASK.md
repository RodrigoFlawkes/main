# Wave Studio — próxima etapa

ID: F2-I6B1-PROOF-3 — prova física silenciosa com diagnóstico de clock
Base de implementação: 97b2c99ee6637b6278cb29b6218248485aa49c25. 696 testes por configuração, 2 Explicit excluídos; 173 direcionados. Revisão estática em REVIEW.md. Esforço Alto para execução; Extra alto para investigação/correção posterior, avisar proprietário. Não confundir instrumentação com correção da causa física.

## Pré-condições

Ler AGENTS.md, PROJECT_STATUS.md, REVIEW.md, docs/ALPHA_ACCEPTANCE.md, docs/WASAPI_BACKEND_PLAN.md, docs/RECOVERY_AUDIT.md. Sincronizar main com fast-forward seguro na pasta oficial. Verificar árvore limpa, HEAD e binário Release correspondente à implementação 97b2c99. Sete casos A01/A02/A03/A05/A09/A11/A12 aprovados por relato pós-formatação, não repetir sem mudança relevante. Prova anterior encerrou em 412,5004ms com InvalidPosition na captura, descarte inicial e transporte efetivo confirmados, ambos Stop/Release. Causa anterior não demonstrada.

Exigir nova autorização específica da prova de cinco segundos, headset conectado, Wave Studio fechado normalmente, ausência de chamadas/testes e endpoints ativos únicos por direção. Verificar ausência de outros runners. Microphone (fifine Microphone) e Alto-falantes (USB Audio Device) se nomes atuais exatos. Não usar fallback ou IDs antigos. Se faltar condição imediata, pedir somente o necessário; sem Start até confirmação. Não instalar plugins/drivers/dependências, elevar privilégios, alterar ExecutionPolicy/segurança, níveis/mute/padrões, mover/excluir pastas ou criar novo backup integral.

## Execução única

Executar somente WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport, NativeSilentTransport, com filtro exato --explicit only --filter-method e runner .NET direto Release. Sem repetir builds/regressões já atualizados e verificados. Variáveis WAVESTUDIO_SILENT_START_AUTHORIZED=1, WAVESTUDIO_SILENT_PREREQUISITES_CONFIRMED=1, WAVESTUDIO_SILENT_CAPTURE_NAME e WAVESTUDIO_SILENT_HEADSET_NAME apenas no processo do teste autorizado; remover após. Cinco segundos, render sempre SILENT, captura em memória, sem armazenamento de voz, sem rota externa. Preservar tratamento de descarte, critérios e proteções. Não encerrar à força nem repetir automaticamente; em cleanup incerto preservar recursos/guard.

Critérios existentes: TransportObserved=true, janela completa, ambos Start/Stop e release, sem recursos retidos/buffers em voo, >=2 avanços válidos de clock em cada papel, captura aceita e FIFO efetivamente produzida/lida, contabilidade terminal coerente. Descarte inicial opcional no máximo um pacote flags 1/3 dentro de 500ms; ausência não é falha. Reportar formato, períodos/capacidades, padding, latência reportada (não fim a fim), frames/pacotes aceitos/descartados/adquiridos/liberados, priming, drops/underruns, duração, HRESULTs e estado final.

## Evidência diagnóstica obrigatória

Preservar JSON original local em artifacts/wasapi-silent, sem IDs/nomes/PCM. Conferir ClockHistory de ambos os owners após join: TotalReadings, OverwrittenReadings, Readings, LastAccepted, Rejection; índices/ordem, raw position/frequency/QPC/HRESULT, tempo desde Start, idade, âncora, deltas, resto, avanço esperado/real, erro/limite, motivo exato. Se falhar, registrar a leitura rejeitada e a anterior aceita com valores numéricos completos quando disponíveis, reproduzir offline o cálculo exato do ramo; distinguir GetBuffer (frames) de GetPosition (DeviceUnits), não inventar dados. Se relatório falhar ou histórico faltar por recursos retidos, declarar limitação e parar. Se passar, resumir variações/maior erro e limite observado por papel, confirmar toda janela; não inferir causa da falha anterior nem drift controlado. Sem mudar código, tolerância, polling, duração, reset, flags ou política. Não substituir prova por simulação.

Atualizar PROJECT_STATUS, CHANGELOG, docs/WASAPI_BACKEND_PLAN, docs/ALPHA_ACCEPTANCE e README com resultado comprovado, incluindo evidência suficiente para revisão offline. Commit/push somente documental autorizado pelo prompt; conferir main limpa e parar. Não editar NEXT_TASK/REVIEW. F2-I6B2/F3 só após nova revisão; se falhar, definir investigação/correção determinística separadamente. Se cota acabar preservar checkpoint seguro.
