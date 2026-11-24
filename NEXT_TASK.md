# Wave Studio — próxima tarefa técnica

**F5-I2C-OWNED-WASAPI-SESSION — integração controlada de captura, render e relógios ao PCM do mixer.**

Base funcional F5-I2B integrada em `8929bc6449b6c5e79a57810dd7e362940a1913c6` (PR #5). [CI Windows x64](https://github.com/Wave-Universal-Studio/main/actions/runs/45337303234): 946/946 Debug e 946/946 Release, zero falhas, 7 Explicit excluídos, builds sem avisos. Anteriores F5-I1 e F5-I2A prontas. Esforço EXTRA ALTO. Autonomia técnica autorizada, mas nenhuma instalação, custo, driver, modificação do Windows ou prova física sem consentimento específico. Issue #2 aberta.

## Contratos reais e sequência

O mixer `BusMixSession` já soma PCM de dois barramentos; `BoundedBusPcmTransport` fornece filas SPSC; `WasapiCaptureBlockAdapter` decodifica PCM16/float32 recebido de owner e `WasapiRenderBlockAdapter` codifica PCM para futuros outputs. F5-I2C deve construir uma costura de ciclo de vida **realista e segura** com os contratos WASAPI existentes: owner MTA por endpoint, aquisição/liberação de buffers sempre no owner, timer/clock QPC verificado, conversão de blocos e descarte na mudança de geração, Stop/cancelamento, hotplug, backpressure, eliminação de feedback e entrega condicionada a tickets. O escopo inicial deve ser **opt-in e limitado**; preservar runner F3 curto e os dispositivos Windows sem alterações por padrão. Investigar primeiro se os contratos `IWasapiPreparationDevice`, `IWasapiStreamClient`, `IWasapiCapturePackets`, `IWasapiRenderPackets` são suficientes; evitar duplicação de backend.

Não inferir fontes PCM isoladas de PID/app pelo Core Audio, nem usar loopback agregado como se fosse canal Game independente. Stream Mix não recebe microfone, chat/retorno de chamada ou fonte agregada sem consentimento explícito; destino externo começa desabilitado. `A+B` não foi detectado no dispositivo do usuário, só VB-CABLE simples. Não garantir Personal+Stream físico simultâneo sem endpoints, provas de clock e viabilidade. Nenhuma captura secreta, nenhum novo serviço sempre ativo na inicialização, nenhum software/driver instalado.

## Entrega e segurança

Priorizar um incremento vertical **executável com dispositivos simulados e owners injetados**, mostrando fluxo de captura nativo simulado -> adapter -> filas -> mixer -> adapter -> render simulado, incluindo tempos, formato e flags. Se implementar backend Windows de verdade, deixá-lo desabilitado e dependente de autorização explícita, com Stop cooperativo e rollback, e testar apenas com fakes no CI. Não fabricar provas de hardware. Nenhum botão de UI sem áudio real; preservar master Windows, Jogo/Comunicação, idiomas pt-BR/en-US/es-ES, EQ de prévia, teste mic e roteamento externo.

Testes adversariais no Windows: herança de thread MTA, ReleaseBuffer em todas saídas, identidade e geração obsoleta, QPC futuro/stale, clock drift, fragmentos de tamanho errado, underrun/overrun, cancelamento em chamada COM, retorno e encerramento, reentrância, loops, silêncio e autorização externa, frames PCM, sem alocação por callback. Somente nativos simulados e transportes injetados; 7 Explicit excluídos. Após último commit, restore locked auditado, builds Debug/Release sem warnings/errors e regressão comum baseline 946 + novos testes por config. PR apenas com CI verde e revisão, sem force push.

Atualizar PROJECT_STATUS, CHANGELOG, docs/ARCHITECTURE, docs/AUDIO_ENGINE_PLAN e docs/TEST_PLAN; **nunca substituir README completo nem seu resumo de três linhas**, incluir no README só recursos efetivamente operacionais. Comentários próprios em pt-BR. ChatGPT mantém REVIEW, NEXT_TASK, CHAT_CONTINUITY. Não exigir microintervenções; continuar em etapas verídicas sem prometer execução ilimitada do Codex Cloud.
