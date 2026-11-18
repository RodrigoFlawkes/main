# Wave Studio — próxima etapa

ID: F2-I6B1-PROOF-2 — nova validação física silenciosa
Base de implementação: c7ff2c97c97ded349a561c9f07bbfa481999b343. 663 testes em cada configuração, 2 Explicit excluídos. Revisão estática em REVIEW.md. Esforço Alto para prova; Extra alto somente se diagnóstico complexo justificar, avisar proprietário.

## Pré-condições

Ler AGENTS.md, PROJECT_STATUS.md, REVIEW.md, docs/ALPHA_ACCEPTANCE.md, docs/WASAPI_BACKEND_PLAN.md e docs/RECOVERY_AUDIT.md. Sincronizar main com fast-forward seguro na pasta oficial. Verificar árvore limpa, HEAD e binário Release correspondente à correção c7ff2c9. Os sete casos manuais A01/A02/A03/A05/A09/A11/A12 foram aprovados pelo proprietário após formatação; não repetir sem mudança relevante. A tentativa anterior não foi aprovada, mas encerrou com Stop/Release confirmados. A correção agora aguarda validação física.

Exigir nova autorização específica de captura curta/saída exclusivamente SILENT, headset conectado, WaveStudio fechado normalmente, ausência de chamadas e testes, nomes exatos únicos e ativos por direção. Verificar ausência de outros runners. Usar Microphone (fifine Microphone) e Alto-falantes (USB Audio Device) somente se os nomes atuais continuarem exatamente correspondentes. Não usar IDs antigos/fallback. Se faltar condição imediata, pedir somente o necessário; não iniciar áudio sem confirmação. Não instalar drivers, plugins externos, alterar segurança, níveis/mute/padrões ou privilégios. Não mover/excluir pastas ou criar novo backup integral.

## Prova única

Executar somente o teste Explicit WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport, categoria NativeSilentTransport, filtro exato --explicit only --filter-method conforme runner instalado. Usar Release da correção, sem repetir regressões ou builds se já verificados e atualizados. Definir WAVESTUDIO_SILENT_START_AUTHORIZED=1, WAVESTUDIO_SILENT_PREREQUISITES_CONFIRMED=1 e WAVESTUDIO_SILENT_CAPTURE_NAME/WAVESTUDIO_SILENT_HEADSET_NAME apenas no processo de teste após confirmação; remover depois. Cinco segundos; saída sempre SILENT; PCM em memória, sem gravação de voz, sem saída audível, sem rota externa. Preservar política Restricted, usar runner .NET direto. Não encerrar processo à força; se Stop/Release incerto preservar recursos/lease. Não repetir automaticamente, não mudar código, parâmetros ou tolerâncias nesta prova.

Critérios: TransportObserved=true, DurationReached, ambos Start/Stop e release, nenhum recurso retido/buffer em voo, pelo menos dois avanços válidos de IAudioClock por papel, contagens terminalmente coerentes, captura aceita e FIFO produzida/lida efetivamente. Descarte inicial opcional de no máximo um pacote, flags 1/3, validado dentro da janela de 500ms; pode ser zero. Informar contagens adquiridas/liberadas/aceitas/descartadas, flags do primeiro descarte, posição/QPC e HRESULTs, formatos/capacidade/padding/períodos, clocks, drops/underruns, duração e estado final. Verificar contabilidade de aceitos+descartados versus liberados e produção/leitura pós-descarte. Não tratar ausência de descarte como erro; não considerar descarte prova de transporte. Distinguir FIFO nominal, latência reportada e latência fim a fim. Sem alegação de drift controlado ou áudio audível. Em falha registrar estágio e preservar diagnóstico, parar para revisão.

Relatório numérico local em artifacts/wasapi-silent, sem IDs/nomes privados/PCM. Atualizar PROJECT_STATUS, CHANGELOG, WASAPI_BACKEND_PLAN, ALPHA_ACCEPTANCE e README com resultado comprovado; commit/push apenas documental conforme autorização. Não editar NEXT_TASK/REVIEW. Não instalar ferramentas externas, não excluir backups, não iniciar F2-I6B2 nem F3.

## Próxima decisão

Revisar resultado antes de F2-I6B2 (monitoramento audível opt-in, sincronização e checkpoint), depois F3 (integração externa), então priorizar equalizadores de áudio/microfone e módulos funcionais conforme plano mestre, sem pular dependências. Windows 11/aceitação Alpha integral pendentes. Não estimar cota restante. Inventário de pastas já existe; preservar exclusão até autorização específica.
