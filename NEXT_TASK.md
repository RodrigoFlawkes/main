# Wave Studio — próxima entrega

**F5-I2C2-OWNED-BUS-SCHEDULER — scheduler opt-in de captura, mixagem e render por owners.**

Base concluída: F5-I2C1 PR #9 `26cfb38b732b7b4fb9c6a9ddf2b5daebbe828906`, com [CI Windows x64](https://github.com/Wave-Universal-Studio/main/actions/runs/45338821387) **954 testes comuns aprovados Debug e 954 Release**, zero falhas, sete Explicit excluídos por configuração e builds sem avisos. Anteriores F5-I1, F5-I2A e F5-I2B integradas. Esforço EXTRA ALTO; autonomia técnica concedida para PR/merge seguro, sem custos ou ação física. Issue #2 registra dependências.

## Escopo

Integrar `BusMixSession`, `BoundedBusPcmTransport`, `WasapiCaptureBlockAdapter`, `WasapiRenderBlockAdapter`, `WasapiBusCaptureOwnerStep` e `WasapiBusRenderOwnerStep` em uma sessão/scheduler **explicitamente ativável**, testável por owners e relógios simulados. Construir captura→validação temporal/geração→fila→mix Personal/Stream→render com cancelamento, Stop, liberação segura e descarte de geração obsoleta. Reutilizar `TransportActivityGuard`, serviços MTA e verificações `WasapiClockAdapter`; não supor sincronização de múltiplos endpoints nem duplicar o backend. Sem Start/Stop de hardware sem autorização e prova.

Política fail-closed em QPC inválido, flags de descontinuidade, buffers incompletos, falha de ReleaseBuffer, backpressure, atrasos e desconexão. Stream externo começa desabilitado, microfone/comunicação/agregado só com consentimento e mix-minus. Não inferir PCM por aplicativo a partir de PID/sessões de volume nem anunciar OBS/Discord real por simulação. Não alterar drivers/padrões Windows, instalar cabo A+B, iniciar captura oculta ou pedir repetição das provas F3 do usuário.

## Qualidade

Ler AGENTS.md, REVIEW.md, CHAT_CONTINUITY.md, PROJECT_STATUS.md, docs/ARCHITECTURE.md, docs/AUDIO_ENGINE_PLAN.md, docs/WASAPI_BACKEND_PLAN.md e docs/TEST_PLAN.md. Comentários pt-BR. Testar com fakes MTA, múltiplos owners, relógios, 1→2 buses, privacidade, underrun, backpressure, Stop, cancelamento, troca de geração e erro de liberação. Nenhum dispositivo físico. GitHub Actions Windows x64 após código consolidado: restore locked auditado, builds Debug/Release limpos, regressão **baseline 954 + novos testes comuns**, sete Explicit excluídos. Preservar README completo com logo e resumo de três linhas. Criar PR e integrar somente após revisão e CI verde, sem force push. Registrar limitações reais e seguir etapa útil seguinte sem pedir microaprovação.
