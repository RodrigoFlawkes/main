# Wave Studio — próxima tarefa aprovada

**F5-I2B-NATIVE-PCM-PACKET-ADAPTER — adaptação de pacotes PCM do Windows aos barramentos internos**

Base: F5-I1 e F5-I2A integradas, última main funcional c7eb383b7293424ea9430455a6e56f8da574f44e. CI F5-I2A: 938 testes comuns Debug, 938 Release, 7 Explicit excluídos em cada, builds limpos (https://github.com/Wave-Universal-Studio/main/actions/runs/45336327862). Autorização do proprietário para decisões técnicas rotineiras, PRs e merges com CI verde. Esforço EXTRA ALTO. Issue #2: https://github.com/Wave-Universal-Studio/main/issues/2.

## Objetivo

F5-I2A implementou BoundedBusPcmTransport com filas SPSC de blocos float32 10 ms e dois barramentos Personal/Stream. F5-I2B deve fornecer conversão e adaptação executáveis de pacotes de áudio nativo WASAPI para os blocos PCM do motor e saída inversa sem emitir áudio indevidamente. Primeiro validar suporte por WasapiPcm.Validate; somente PCM16 LE e float32 LE, 44,1/48 kHz, mono/estéreo, identidade/geração explícitas e limites de buffers. Reutilizar PcmFloatConverter, AudioClock, NominalConversionStage, ChannelAdapter e contratos atuais sem recriar APIs. Considerar flags SILENT, timestamp inválido, buffer incompleto e geração obsoleta como falhas fechadas ou silêncio explícito, sem fabricar origem separada de app. Não supor que o VB-CABLE simples oferece dois destinos simultâneos.

## Fronteira segura

Não abrir WASAPI automaticamente, não transformar runners de cinco segundos em serviço contínuo, não modificar padrões Windows, instalar drivers, capturar microfone silenciosamente nem assumir OBS/Discord funcional. Se a saída nativa depender de requisitos não presentes, entregar adaptação de pacotes em código de produção, teste injetado e diagnóstico da dependência, sem botão UI fictício. Consumos externos nunca recebem microfone/chat/agregado sem autorização explícita e proteção de retorno de chamada (mix-minus). Qualquer render futuro revalida ticket de geração no instante da entrega. Não pedir testes físicos/reboot que o proprietário já recusou.

## Qualidade e entrega

Ler AGENTS.md, REVIEW.md, CHAT_CONTINUITY.md, PROJECT_STATUS.md, docs/REQUIREMENTS.md, docs/ARCHITECTURE.md, docs/AUDIO_ENGINE_PLAN.md, docs/WASAPI_BACKEND_PLAN.md, docs/TEST_PLAN.md, fontes reais e contratos. Comentários de código sempre pt-BR. Testar com frames PCM sintéticos, formatos/flags válidos e inválidos, mono/estéreo, 44,1/48, clipping, NaN/Inf, backpressure, geração, cancelamento, Stop, ausência de alocações em callback após warmup. Confirmar duas saídas isoladas e falha fechada de fluxo sensível. Testes Windows x64 finais via GitHub Actions (baseline 938 + novos casos, 7 Explicit excluídos, restore locked auditado, builds zero warnings), sem executar hardware. Atualizar documentação técnica, status e changelog; preservar README completo com logo e resumo de três linhas. Branch e PR verificáveis, CI verde antes de merge, sem force push nem mudanças com custo. Próximas tarefas somente após revisão dos resultados, sem invenção de streaming contínuo.
