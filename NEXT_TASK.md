# Wave Studio — próxima tarefa

ID: F1-I4
Base de código revisada: c21db3f
Estado: pronta para execução mediante autorização explícita do proprietário.
Esforço recomendado do Codex: Alto. Se o trabalho revelar bloqueio complexo de concorrência/COM, reportar antes de ampliar escopo.

## Fluxo
- Confirmar remoto Wave-Universal-Studio/main, branch main e git status. Se árvore limpa, sincronizar com git pull --ff-only. Se houver divergência ou alterações locais, preservar e reportar; não resetar nem forçar.
- Ler AGENTS.md, docs/REQUIREMENTS.md, docs/ARCHITECTURE.md, docs/IMPLEMENTATION_PLAN.md, docs/TEST_PLAN.md, PROJECT_STATUS.md e REVIEW.md. A revisão mais recente é a de F1-I3.
- Executar somente F1-I4 quando o proprietário autorizar; se já executada, não repetir. Tratar as correções pertinentes da revisão e não modificar NEXT_TASK.md nem REVIEW.md.
- Não instalar drivers, elevar privilégios ou alterar configurações críticas sem autorização. Commit/push somente quando autorizado. Não iniciar F1-I5.

## Objetivo: medidores reais, hotplug e recuperação
Implementar medidores de pico do Windows e detecção automática de mudanças de endpoints, preservando controles e mixer existentes.

### Entrega funcional
1. Medidores de pico reais para os endpoints selecionados de reprodução/captura por IAudioMeterInformation via NAudio.Wasapi 3.1.0. Se a API/wrapper permitir com baixo custo, medidor de sessão individual no mixer, sem captura de amostras; caso contrário, registrar limitação e não simular. Distinguir pico de RMS/LUFS, ganho, volume escalar e clipping. Zero é leitura válida; indisponível é estado distinto. Limitar valores válidos, rejeitar NaN/Infinity, representar percentual ou dBFS de pico com silêncio tratado sem números inválidos.
2. UI pt-BR com barras de nível reais e indicação de indisponibilidade; atualizações de medição aproximadamente 10 Hz inicialmente, ajustáveis conforme custo medido. Não recriar a grid a cada pico, evitar alocações excessivas/flicker, reduzir consultas ocultas e não competir com edição/volume. Mostrar dados do alvo correto após troca de seleção, sem números antigos de outro endpoint.
3. Registrar IMMNotificationClient para adição, remoção, mudança de estado, padrão (multimídia e comunicações) e propriedades relevantes. Usar APIs públicas verificadas no pacote. Callbacks apenas sinalizam/coalescem eventos; não bloquear nem acessar UI ou COM de forma inadequada no callback. Reconciliar catálogo de forma serializada, com atraso curto para rajadas e fallback manual; se inscrição falhar, reportar e implementar fallback moderado e explícito.
4. Implementar ciclo de vida COM claro: worker MTA de longa duração para objetos persistentes/notificações quando necessário, snapshots desacoplados, registro e cancelamento de inscrição, descarte ordenado no proprietário, sem COM atravessar threads. Definir como filas existentes interagem com o worker, evitando deadlock, starvation e uso após descarte. Não reabrir todos os wrappers a cada tick de medidor sem justificar/medir. Mudança de seleção cancela tarefas antigas e invalida snapshots por geração. Falhas isoladas não derrubam UI.
5. Hotplug e recuperação: preservar seleção explícita de endpoint indisponível e recuperar ao retornar; não mudar silenciosamente para outro. Quando em modo seguir padrão (ou seleção automática inicial), reconciliar mudança de padrão separadamente por direção; sinalizar padrão multimídia/comunicações reais. Não alterar padrão do Windows, volume ou mute durante refresh/hotplug/retomada. Atualizar mixer e controles após remoção/retorno, desabilitar comandos inválidos e preservar preferência.
6. Tratar suspensão/retomada (notificação pública apropriada ou recuperação documentada), falhas transitórias, ausência de dispositivos, encerramento durante eventos e reconexão. Notificações de sessões podem ser adicionadas se viáveis e seguras; polling moderado para reconciliação é aceitável se documentado. Priorizar estabilidade em vez de reescrita excessiva.
7. Corrigir pontos da revisão F1-I3: reconciliação da grid por chave, geração e eventos de edição, falha de sessão não relacionada, seleção automática versus explícita e ciclo de vida COM. Preservar funcionalidades existentes, design simples e acessibilidade. Nenhum efeito, driver, roteamento, gravação, ganho digital ou medidor fictício.

## Verificação
- Testes determinísticos com backends simulados: picos válidos/silêncio/indisponível/NaN, notificações duplicadas e em rajada, adição/remoção, mudança de padrão por direção, endpoint ausente/retorno, geração atrasada, cancelamento, recuperação, erro de inscrição, encerramento com callback/operação pendente, descarte idempotente, nenhuma escrita implícita; regressão dos 77 testes e pontos de UI/sessão da revisão.
- Integração Windows de leitura: consultar medidores reais de captura/reprodução e sessão se suportada, testar inscrição/remoção de callback sem modificar configurações e registrar valores realmente observados. Sem sinal ativo, zero pode ser legítimo; não afirmar que medidor reagiu a fala/música sem evidência. Teste físico com usuário: reprodução e fala, desconectar/reconectar USB, mudança de padrão pelo Windows, suspensão/retomada, mixer com dois apps, sliders e DPI. Não executar alterações físicas ou suspender a máquina sem autorização. Registrar/restaurar níveis anteriores nos testes de escrita.
- Medir custo de atualização e estabilidade (tempos de parede, CPU/RAM se mensuráveis, amostra/duração, número de consultas/assinaturas e descarte). Não prometer latência de DSP nem CPU universal; não usar loops ocupados. 
- Executar dotnet restore WaveStudio.slnx --locked-mode; dotnet build WaveStudio.slnx -c Debug --no-restore; com TESTINGPLATFORM_TELEMETRY_OPTOUT=1, dotnet test --solution WaveStudio.slnx -c Debug --no-build; git diff --check. Corrigir regressões. Manter lock files.
- Atualizar PROJECT_STATUS.md, CHANGELOG.md, docs/ARCHITECTURE.md, docs/TEST_PLAN.md, docs/IMPLEMENTATION_PLAN.md e README conforme necessário. Preservar requisitos, distinguir histórico de estado atual e testes simulados/integrações físicas. Não declarar validação visual se Computer Use falhar; não incluir credenciais ou bin/obj.
- Se publicação for autorizada, commit sugerido: feat: add real audio peak meters and device hotplug recovery. Push normal e confirmar sincronização. Se falha crítica, reportar bloqueio em vez de declarar concluído.

## Critério de saída
Medidores de endpoints reais implementados e consultados, detecção automática de mudanças e recuperação implementadas com ciclo de vida seguro, build/testes aprovados e pendências físicas explicitadas. Não iniciar F1-I5 (persistência JSON) sem nova orientação. O redesign visual profissional permanece previsto após a Fase 1.
