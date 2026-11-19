# Wave Studio — próxima etapa

ID: F2-I6D-AUDIBLE-PROOF — primeira prova física audível nominal limitada
Base funcional 0c1e54b6137d00f6389b70c93b74f1713a5c0a0f. F2-I6D-ALLOC-MONITOR aprovado em revisão, mitigação da medição de alocações aceita no escopo curto, regressões padrão 806/806 em Debug e duas seeds Release, 5 Explicit excluídos. Esforço Alto. Este documento não autoriza Start. Uma única tentativa depende de nova autorização expressa do proprietário para captura temporária e reprodução audível e de confirmação pessoal imediata.

## Preparação sem Start

Sincronizar por fast-forward seguro, verificar raiz oficial, HEAD/origin/main, árvore limpa, SDK, preservar alterações/artefatos/backups. Ler AGENTS, REVIEW, PROJECT_STATUS, NEXT_TASK e seção F2-I6D de docs/WASAPI_BACKEND_PLAN. Confirmar código idêntico à base 0c1e54b (commits posteriores apenas documentais), apphost e DLLs Release x64 correspondentes, restore locked auditado/build Release se necessário para comprovar proveniência. Sem mudança de código, runtime, drivers, Windows ou política Restricted. Não repetir regressões comuns, nominal SILENT, prova 4, DIAG-PROOF ou sete casos manuais já aprovados. Não instalar ferramentas.

Descobrir exatamente um método no apphost Release x64 com --list-tests text --filter-method WaveStudio.Tests.WasapiNativeAudibleProofTests.AuthorizedHeadsetRunsBoundedAudibleNominalProof. Nenhum outro Explicit. Conferir que o console de execução é interativo, visível, de primeiro plano e acessível fisicamente ao proprietário, sem redirecionar stdin. Se não for possível, parar antes de Start e entregar ao proprietário comando local PowerShell seguro com opt-ins somente do processo, filtro único e instruções de execução manual. Não automatizar Esc/Enter nem usar console em segundo plano, execução remota sem operador, script que injete teclas ou contorne o teste.

Confirmar imediatamente com o proprietário: headset USB físico conectado e colocado, sem caixas abertas ou risco de feedback, volume baixo confortável confirmado pelo próprio usuário, microfone conectado, Wave Studio fechado normalmente, ausência de chamadas/gravações/outros testes/runners, sete casos manuais ainda aplicáveis (A01,A02,A03,A05,A09,A11,A12). Confirmar os dois nomes exatos de captura/saída, sem presumir que permaneçam iguais. Conferir endpoints ativos, únicos por direção e capacidade/mix exatos, sem fallback. Autorização específica de até 5 s, captura temporária em memória, reprodução audível nominal e ganho digital máximo 0,05, nenhuma voz armazenada. O teto digital não é garantia de SPL seguro. Nunca alterar volume/padrão Windows por conta própria. Se condição faltar ou operador não estiver presente, não executar.

## Única tentativa física autorizável

Definir somente no ambiente do processo de execução, após as respectivas confirmações:
- WAVESTUDIO_AUDIBLE_PROOF_START_AUTHORIZED=1
- WAVESTUDIO_AUDIBLE_PROOF_PREFLIGHT_CONFIRMED=1
- WAVESTUDIO_AUDIBLE_PROOF_HEADSET_CONFIRMED=1
- WAVESTUDIO_AUDIBLE_PROOF_LOW_LEVEL_CONFIRMED=1
- WAVESTUDIO_AUDIBLE_PROOF_CAPTURE_NAME (nome exato confirmado)
- WAVESTUDIO_AUDIBLE_PROOF_HEADSET_NAME (nome exato confirmado)

Usar apphost Release x64 direto, console interativo em primeiro plano, --explicit only --filter-method com o nome completo acima e --timeout 45s, diagnóstico conforme roteiro e watchdog compatível com entrada interativa, sem matar processo em limpeza incerta. Não redirecionar stdin. O proprietário deve pressionar Esc pessoalmente para testar parada, depois Enter para confirmar dentro de 15 s; segundo Esc antes de Enter cancela. Não injetar teclas. Durante a prova, falar em tom normal, sem elevar a voz ou aumentar volume para forçar detecção. Se ouvir feedback, nível imprevisto, desconforto ou quiser parar, pressionar Esc imediatamente; cancelamento cooperativo invalida ticket e preserva cleanup/guard. Não prometer revogação de PCM já submetido. Nenhum restart/retry, mesmo se não ouvir ou o runner falhar. Duração configurada 5 s, ganho inicial zero, ativação só após prefill e corte coerente, teto 0,05, razão nominal. Não habilitar drift.

## Critérios e análise offline na mesma etapa

Registrar código de saída, runner e tempo QPC, DurationReached/NominalObserved, PacketRateConfirmed, RenderSilent (esperado false neste modo), DriftPrecisionAvailable=false, DriftControlEnabled=false e Cleanup.TransportObserved=false. Conferir movimento, continuidade, janelas nominais, motivos de indisponibilidade/falha, FIFO, margem, cortes/frescor, zero drops/underruns, Start/Stop/Get/Release, ausência de recursos retidos/buffers em voo. Não inventar distribuição ou lacuna não registrada. Confirmar contabilidade: frames render liberados = SilentFrames + PcmFrames; NonZeroFrames >0 e <= PcmFrames; PeakGain >0 e <=0,05; PeakAmplitude >0 e <=0,05 + meio passo PCM16 conforme runner, informar formato e valor real; clipping FIR registrado e interpretado separadamente, não confundir com clipping físico ou pico acústico. Informar warmup, priming, saldo, ocupação intervalar e evidência efetivamente disponível; não converter fila/latência algorítmica em latência acústica. Critérios numéricos não provam percepção.

Obter relato pessoal do proprietário: ouviu a própria voz pelo headset? nível confortável? timbre claro ou distorção/ruído? atraso percebido? feedback/desconforto? houve necessidade de Esc? Não supor relato positivo por código 0. Sem confirmação de escuta, registrar resultado automatizado e aceitação manual pendente. Se houver desconforto/feedback ou cancelamento, não classificar como prova completa e não repetir. Se falhar, preservar resultado e motivo, não alterar limites. Não armazenar voz/PCM/IDs/nomes em Git ou logs publicados. JSON e log em artifacts/wasapi-audible-proof ignorado; registrar hash e caminho, sem anexar áudio.

## Saída

Atualizar PROJECT_STATUS, CHANGELOG e WASAPI_BACKEND_PLAN com fatos, comandos, critérios, relato manual, limitações, base funcional e estado local/remoto. Commit/push apenas documental se autorizado; não modificar REVIEW, NEXT_TASK, CHAT_CONTINUITY, código ou asserções. Encerrar para revisão. Se preflight impedir Start, reportar bloqueio e não consumir tentativa física inexistente. Se execução ocorreu, autorização consumida independentemente do resultado. Sem repetição de provas ou regressões.

A prova, se aprovada, valida apenas primeira reprodução audível nominal limitada no par/formatos testados. Não aprova monitoramento contínuo, drift físico adaptativo, latência acústica medida, compatibilidade universal, Fase 2 completa ou F3. Próxima decisão após revisão: caminho mínimo para fechar critérios F2 e chegar à integração externa, com pendências explícitas e sem ciclo de provas repetitivas. Preservar todos requisitos, design e Config → Idioma na primeira UI pós-F3.
