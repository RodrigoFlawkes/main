# Wave Studio — próxima etapa

ID: F2-I6C-NOMINAL-PROOF — prova física nominal limitada, SILENT, tentativa única
Base funcional 287b05d4f340bc3dcd91ef18be53fc678b09932c. F2-I6C-NOMINAL-MONITOR aprovado em simulação; Debug 801/801 e Release serial 801/801, Release padrão com falhas de alocação ainda abertas. Esforço Alto. Este documento não autoriza Start: autorização expressa de uma tentativa e confirmação imediata do proprietário são indispensáveis.

## Preparação sem Start

Sincronizar por fast-forward seguro, conferir raiz oficial, HEAD/origin/main, árvore limpa, SDK, preservar alterações/artefatos/backups. Ler AGENTS, REVIEW, PROJECT_STATUS, NEXT_TASK e seção F2-I6C de docs/WASAPI_BACKEND_PLAN. Confirmar código idêntico à base 287b05d (commits posteriores apenas documentais); binário Release x64 correspondente, restore locked auditado/build Release somente se necessário para comprovar proveniência. Não modificar código, limites, asserções, Windows, drivers ou política Restricted. Não repetir regressões comuns, prova 4, DIAG-PROOF ou sete casos manuais já aprovados.

Descobrir exatamente um método no runner/apphost .NET Release x64 com --list-tests text --filter-method e o nome completo WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedEndpointsRunBoundedSilentNominalTransport. Nenhum outro Explicit.

Obter autorização específica e confirmação imediata do proprietário: headset e microfone conectados, Wave Studio fechado normalmente, sem chamadas/gravações/testes/outros runners, sete casos manuais ainda aplicáveis. Confirmar captura temporária em memória por sessão de cinco segundos, saída sempre SILENT, razão nominal, nenhum armazenamento de voz. Conferir par de endpoints ativo, exato e único por direção, formato exato, sem fallback; não presumir conexão ou mudar dispositivo automaticamente. Se alguma condição faltar, parar antes de Start.

## Única tentativa física

Somente após autorização e pré-condições verificadas, definir exclusivamente no processo do runner:
- WAVESTUDIO_NOMINAL_SILENT_START_AUTHORIZED=1
- WAVESTUDIO_NOMINAL_SILENT_PREFLIGHT_CONFIRMED=1
- WAVESTUDIO_NOMINAL_SILENT_CAPTURE_NAME (nome exato verificado)
- WAVESTUDIO_NOMINAL_SILENT_HEADSET_NAME (nome exato verificado)

Executar somente o método descoberto com --explicit only --filter-method, apphost Release x64 e timeout/watchdog seguro conforme protocolo já adotado. Duração configurada 5 s, nenhuma correção ppm, nenhum áudio audível ou encoder nativo, sem persistir PCM. Uma tentativa; falha não autoriza retry. Se buffer em voo/liberação incerta, manter protocolo de cleanup/guard; não forçar descarte nem encerrar apps de terceiros.

## Critérios e análise offline na mesma entrega

Registrar código de saída, tempo runner, tempo QPC observado, DurationReached, NominalObserved, RenderSilent, PacketRateConfirmed, DriftPrecisionAvailable=false, Cleanup.TransportObserved=false e DriftControlEnabled=false; não atribuir aprovação B1/B2 à prova nominal. Confirmar movimento dos dois papéis, continuidade dos pacotes aceitos, janelas nominais de pacotes e rejeições/indisponibilidades do clock; se houver fase indisponível, recalcular e informar orçamento acumulado com timestamps disponíveis, não inventar dado ausente. Rejeições fatais de integridade não podem ser aceitas. Conferir Stop/Release confirmados, HRESULTs, ausência de buffers em voo, erros de limpeza ou recursos retidos, zero drops/underruns, produção/leitura positivas, contabilidade de captura e render (priming endpoint, pré-ticket, warmup, tentativas FIFO), saldo terminal. Conferir margem e limites conservadores de bloco, cortes coerentes, frescor e cobertura usando dados efetivamente registrados. Distinguir min/max/média intervalar da ocupação de latência acústica. Não alegar distribuição de intervalos ou lacuna máxima se relatório agregado não a expõe; explicitar limite de observabilidade. Reportar limiares de fila e motivo de parada se falhar, sem alterar critérios. Uma prova aprovada valida exclusivamente transporte nominal limitado para o par e formato testados, não drift físico, estabilidade longa, monitoramento audível nem universalidade.

Preservar JSON/log original em artifacts/wasapi-nominal-silent ignorado, calcular hash e não versionar nomes/IDs/voz/PCM. Atualizar PROJECT_STATUS, CHANGELOG e WASAPI_BACKEND_PLAN com fatos, comandos, resultado e limitações; documentar bloqueio se falhar antes de Start. Sem novo código. Se houver logs anteriores acessíveis, resumir evidência útil de alocação (contexto padrão/serial, seed, teste/thread/bytes) sem repetir suítes, alterar runtime ou apresentar hipótese como conclusão. A investigação direcionada com correção demonstrável será próxima implementação após revisão, não liberar áudio com base em execução serial. Commit/push exclusivamente documental se autorizado, sem force push. Não alterar REVIEW, NEXT_TASK ou CHAT_CONTINUITY. Encerrar para revisão.

## Fora do escopo

Sem repetição de provas, alteração de código, monitoramento físico audível, controle adaptativo no hardware, F3 ou novas funcionalidades/UI. Alocações intermitentes Release continuam bloqueando monitoramento físico audível/contínuo; a prova SILENT limitada não as resolve. Próxima implementação, se evidência nominal satisfatória: diagnóstico objetivo e correção de alocação/medição comprovada, agrupando preparação do monitoramento audível curto apenas se requisitos de segurança e regressão forem atendidos. Se a prova falhar, revisar causa e caminho mínimo sem repetir cegamente. Preservar prova 4, DIAG-PROOF reprovada, B2 simulado, requisitos do produto, roadmap e internacionalização pós-F3.
