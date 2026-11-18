# Wave Studio — guia de continuidade para o ChatGPT

> **Leia primeiro ao assumir um novo chat do projeto WaveStudio.** Este arquivo é a passagem de contexto do próprio ChatGPT, não uma tarefa do Codex. Repositório privado oficial: [Wave-Universal-Studio/main](https://github.com/Wave-Universal-Studio/main), branch `main`.

Criado em 18/11/2026 na transição do WaveStudio P1. O histórico integral de chats pode não estar acessível. Não presumir leitura completa. As decisões e evidências versionadas são a memória operacional do projeto. Esta fotografia inicial é histórica; sempre consultar o HEAD atual antes de trabalhar.

## 1. Missão e papéis

Wave Studio (Wave Universal Studio, executável WaveStudio.exe) é uma central universal de áudio para Windows, independente de fabricante, com controle real, DSP, mixer, roteamento e personalização para jogos, comunicação, música, filmes e streaming. Produto funcional, acessível e expansível, não protótipo com controles fictícios.

- **Proprietário:** define prioridades, autoriza execução, publicação e operações sensíveis, realiza validações físicas quando necessárias. Conhece Java/JavaScript básicos e está aprendendo C#. Comunicar em português brasileiro de forma direta e acessível.
- **ChatGPT neste projeto:** responsável por planejamento, revisão técnica estática, conferência de commits/diffs/evidências, identificação de riscos e dependências, registro em REVIEW.md e preparação/publicação de NEXT_TASK.md no GitHub quando solicitado. Não implementar paralelamente o trabalho destinado ao Codex. Não afirmar execução no computador do proprietário.
- **Codex:** sincroniza a pasta oficial, lê instruções versionadas, implementa somente a etapa autorizada, compila, testa, documenta resultados em PROJECT_STATUS.md/CHANGELOG.md e publica se autorizado. Não altera NEXT_TASK.md ou REVIEW.md sem solicitação expressa. Encerra após cada checkpoint.
- **Coordenação:** o ChatGPT envia instruções detalhadas pelo GitHub; o proprietário envia ao Codex apenas prompt curto com checkpoint/commit, esforço e autorização. O proprietário traz o relatório ao ChatGPT para revisão. Não há comunicação automática ou trabalho em segundo plano.

## 2. Checklist obrigatório de retomada

- [ ] Recuperar o contexto disponível de conversas anteriores do projeto, especialmente o chat imediatamente anterior. Não confundir resumo com transcrição integral. Dizer quais fontes realmente foram consultadas.
- [ ] Acessar o repositório privado pelo conector autorizado do GitHub. Verificar HEAD completo da main, árvore versionada, último commit e histórico desde a última revisão. Se sem acesso, reportar e solicitar somente o material indispensável; não inventar status.
- [ ] Ler integralmente AGENTS.md, NEXT_TASK.md, REVIEW.md, PROJECT_STATUS.md, README.md, CHANGELOG.md, requisitos, plano mestre, arquitetura, plano de implementação, testes e aceitação; ler também documentos técnicos da fase e consultar os demais documentos do índice para uma revisão geral. Recuperar partes relevantes truncadas/paginadas. Não confundir afirmações históricas com o estado atual.
- [ ] Identificar última implementação funcional, commits documentais, última revisão, execução pendente/em curso, aprovações, bloqueios, riscos e autorizações consumidas. Conferir código/testes pertinentes ao diff; ampliar revisão conforme risco. Não dizer que auditou linha a linha todo o código se não o fez.
- [ ] Comparar relato Codex com SHA, diff, critérios e evidências. Separar claramente resultado relatado, verificação estática própria, teste automatizado, teste físico e aceitação ainda pendente. Não inventar testes.
- [ ] Apresentar ponto de retomada ao proprietário, escopo confirmado e limites. Se já houve execução, revisar antes de planejar outra. Se tarefa ainda pendente, continuar nela. Não reiniciar fases ou repetir provas aprovadas sem mudança relevante/justificativa.
- [ ] Conferir roadmap e todos os requisitos antes de decidir a próxima tarefa. Não omitir pedidos, antecipar funcionalidades ou mudar prioridades silenciosamente.

## 3. Fontes de verdade e índice de leitura

| Arquivo | Função |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Regras normativas para agentes, segurança, idioma e fluxo |
| [NEXT_TASK.md](NEXT_TASK.md) | Próxima tarefa, critérios, restrições e validações |
| [REVIEW.md](REVIEW.md) | Pareceres, riscos, aprovações condicionais e decisões |
| [PROJECT_STATUS.md](PROJECT_STATUS.md) | Estado e resultados efetivos registrados pelo Codex |
| [CHANGELOG.md](CHANGELOG.md) | Histórico de alterações |
| [README.md](README.md) | Visão do produto e resumo de estado |
| [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) | Requisitos normativos e critérios das fases |
| [docs/PRODUCT_MASTER_PLAN.md](docs/PRODUCT_MASTER_PLAN.md) | Escopo completo e navegação definitiva |
| [docs/PRODUCT_HUB_PLAN.md](docs/PRODUCT_HUB_PLAN.md) | Hub, catálogo e personalização |
| [docs/FUTURE_AUDIO_FEATURES.md](docs/FUTURE_AUDIO_FEATURES.md) | Efeitos e funcionalidades futuras |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Arquitetura e contratos |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | Sequência e decisões de implementação |
| [docs/AUDIO_ENGINE_PLAN.md](docs/AUDIO_ENGINE_PLAN.md) | Motor, formatos, buffers, relógios |
| [docs/WASAPI_BACKEND_PLAN.md](docs/WASAPI_BACKEND_PLAN.md) | Backend físico, critérios e evidências |
| [docs/CLOCK_DRIFT_SIMULATION.md](docs/CLOCK_DRIFT_SIMULATION.md) | Simulação de relógios/drift |
| [docs/RESAMPLING_QUALITY.md](docs/RESAMPLING_QUALITY.md) e [adaptativo](docs/ADAPTIVE_RESAMPLING_QUALITY.md) | Conversão e qualidade |
| [docs/EQ_PREVIEW.md](docs/EQ_PREVIEW.md) | EQ do teste curto, perfis e limites |
| [docs/TEST_PLAN.md](docs/TEST_PLAN.md) e [docs/ALPHA_ACCEPTANCE.md](docs/ALPHA_ACCEPTANCE.md) | Testes e aceitação manual |
| [docs/GENERAL_CHECKUP.md](docs/GENERAL_CHECKUP.md) | Auditoria e ciclo de vida |
| [docs/RECOVERY_AUDIT.md](docs/RECOVERY_AUDIT.md) | Recuperação pós-formatação, inventário e preservação |
| [docs/FEASIBILITY.md](docs/FEASIBILITY.md) e [licenças](docs/THIRD_PARTY_NOTICES.md) | Viabilidade e distribuição |
| [assets/branding/README.md](assets/branding/README.md) | Identidade visual e ícone oficial |

AGENTS e REQUIREMENTS governam regras e requisitos; o plano mestre consolida escopo e navegação. Commits, evidências e últimas seções de status/revisão determinam o estado real. NEXT_TASK pode ainda apontar para uma tarefa já executada, pois o Codex não o modifica. README e introduções de planos podem estar atrasados. Não apagar histórico para resolver divergências. Resolver conflitos por evidência e, se envolver decisão do proprietário, solicitar orientação. Nenhum arquivo autoriza por si só execução, commit/push do Codex ou operação sensível.

## 4. Ciclo obrigatório de cada incremento

1. Receber relatório Codex, conferir HEAD, base, diff e estado. Confirmar se commit realmente existe e se main corresponde. Não confundir relato de árvore local limpa com verificação remota.
2. Revisar escopo, alterações, contratos, segurança, testes novos e regressões, pendências anteriores. Conferir especialmente concorrência, recursos, áudio físico, persistência e consequências para outras fases quando pertinentes.
3. Emitir parecer explícito: aprovado dentro do escopo, aprovado com condições ou bloqueado. Registrar limitações e o que não foi testado. Se falhou, não avançar; definir correção/diagnóstico restrito e preservar evidências.
4. Anexar revisão datada em REVIEW.md (não substituir histórico), com SHA, evidências, riscos, decisão e dependências. Preparar NEXT_TASK.md com ID único, base, objetivo, escopo, critérios, testes, proibições, saída e etapa seguinte bloqueada. Atualizar outros documentos somente se pertinente. Não modificar código-fonte no commit de planejamento.
5. Quando proprietário solicitar publicação das instruções, conferir HEAD imediatamente antes, publicar commit documental na main, sem force push, e verificar o novo HEAD/diff. Se houver divergência, não sobrescrever; reavaliar.
6. Enviar ao proprietário o hash/link, esforço Codex e prompt curto. Não colar toda tarefa novamente se ela já está no GitHub. O prompt deve explicitar o checkpoint e as autorizações necessárias; não incluir autorização física genérica.
7. Codex executa, valida e registra PROJECT_STATUS/CHANGELOG, publica conforme autorização e encerra. Voltar ao passo 1. Não planejar vários incrementos como uma execução única.

### Modelo de prompt curto ao Codex

```text
Sincronize o repositório com segurança e leia AGENTS.md, REVIEW.md,
PROJECT_STATUS.md e NEXT_TASK.md. Execute somente [ID da tarefa],
conforme commit [SHA]. Autorizo [escopo exato, validações, builds,
testes comuns, commit/push se solicitado]. Não execute provas físicas
nem avance para a próxima etapa sem autorização específica.
Ao finalizar, informe resultados reais, pendências, SHA/link e
status local/remoto; encerre para revisão.
```

Adaptar o modelo ao checkpoint real, incluindo autorizações específicas se o proprietário as conceder. Não inventar consentimento. Provas físicas exigem prompt separado, preflight imediato e limites próprios.

## 5. Preferências e decisões permanentes

- Esforço **Alto** como padrão; **Extra alto** para COM, concorrência, diagnóstico difícil, arquitetura ou risco relevante, com explicação e decisão do proprietário. Não prometer consumo exato de cota. Economizar com tarefas pequenas, prompts curtos e evitando repetições desnecessárias; nunca sacrificar testes, segurança ou qualidade. Se cota acabar, registrar checkpoint e retomar depois, sem publicar trabalho não validado como pronto.
- Prioridade: consolidar produto funcional bruto, com interface simples e acessível; redesign profissional por último. Ajustes de usabilidade necessários são permitidos. Não multiplicar efeitos antes de validar integração externa F3.
- Todo comentário explicativo próprio (C#, testes, PowerShell, configs e XML) em pt-BR. Preservar identificadores, APIs, strings funcionais, diagnósticos, diretivas, comandos, referências XML, licenças e terceiros/gerados. Tradução antiga em checkpoint separado, sem alterar lógica.
- UI pt-BR por padrão. Config → Idioma obrigatório na primeira etapa de UI pós-F3, não depois do redesign. Recursos por cultura, seleção manual/seguir Windows, persistência, fallback pt-BR, traduções locais revisadas e completas; ampliar ao máximo viável, sem opções fictícias. Prioridades propostas pt-BR, en, es, pt-PT, fr, de, it; depois zh-Hans/zh-Hant, ja, ko, ru, ar, hi, tr, pl, uk e outras. Testar Unicode, expansão, DPI, teclado, RTL e fallback. Não alterar motor/contratos por idioma.
- Marca oficial ciano/azul/roxo e ícones originais preservados, inclusive exe, janela, barra de tarefas e Alt+Tab conforme suporte. Não gerar substituição arbitrária.
- Produto local, sem anúncio, conta obrigatória, coleta/envio de áudio ou paywall de recursos planejados. Apoiar é voluntário no rodapé. Sem copiar código, marcas, firmware ou UI proprietária; verificar licenças.
- Independência de fabricante. FIFINE AM8 e headset USB são referências de hardware, não restrições. Compatibilidade Voicemeeter, Equalizer APO/Peace e outras referências é objetivo a validar, não promessa. Não emular phantom power/ganho analógico ou telemetria física inexistente.
- Navegação definitiva: Hub, Áudio, Microfone, Mixer, Reparos, Config; Apoiar no rodapé. Voz e tratamento não são abas independentes. Soundboard em Microfone com acesso contextual no Mixer e estado único. Toda aparência/animação/cor/wallpaper/idioma em Config. Modos simples/avançado e EQ básico/avançado/personalizado conforme plano mestre.

## 6. Escopo do produto — nunca perder funcionalidades

O plano mestre e REQUIREMENTS são a especificação detalhada; esta lista é um índice de cobertura, não substituição nem declaração de implementação.

| Módulo | Escopo a preservar |
| --- | --- |
| Hub | Meu Equipamento, cartões de dispositivos, catálogo visual expansível, associação manual, imagens licenciadas, nome personalizado, status/perfil/atalhos, telemetria somente real |
| Áudio | Padrão/seleção, volume/mute/balanço/canais, formatos/amostragem, medidores e teste; EQ 10/31 bandas, paramétrico e personalizável, presets, preamp, A/B, bypass, espectro; dinâmica, graves/brilho/clareza, ambientes/reverb, pitch/karaokê com limites; surround/HRTF/shifter |
| Microfone | Ganho digital/boost, EQ, gate, compressor, limiter, redução de ruído, de-esser, AGC/AEC quando viável, saturação, teste curto, monitoramento opt-in, efeitos vocais/pitch/formantes, soundboard |
| Mixer | Sessões reais, faders, master independente, pan/mute/solo, VU, clip, canais MIC/GAME/CHAT/MUSIC/MEDIA/SYSTEM/AUX, game/chat balance, barramentos, matriz, mix-minus, ducking, monitoramento independente, gravação multifaixa |
| Reparos | Diagnóstico, problemas de dispositivos/áudio, correções seguras e restauração |
| Config | Temas, alto contraste, seguir Windows, cores, wallpaper local, animações, idioma, sistema, atalhos, perfis, backup e privacidade |
| Integrações | DSP real de reprodução externa e microfone processado em OBS/Discord; avaliar APO, endpoint virtual, VST3, AutoEQ, compatibilidade com apps e licenças |

Ver detalhes de faixas, filtros, presets, canais, efeitos, catálogo e critérios nos documentos originais. Não reduzir escopo silenciosamente. Cada requisito novo deve ser acrescentado ao documento normativo pertinente e refletido no planejamento sem quebrar dependências.

## 7. Roadmap e dependências

| Fase | Entrega |
| --- | --- |
| 0 | Viabilidade, arquitetura, requisitos, licenças |
| 1 | Controles Windows, endpoints, sessões, medidores, hotplug, configurações, identidade e Alpha |
| 2 | Motor, PCM, buffers, conversão, sincronização, WASAPI, monitoramento, latência |
| 3 | Prova externa real: reprodução de navegador processada e microfone em OBS/Discord, sem loop e com restauração |
| 4 | Mesa básica, faders/canais/master/game-chat/solo, bus pessoal |
| 5 | Múltiplos endpoints, roteamento/barramentos, matriz, mix-minus, ducking |
| 6 | EQ headset e dinâmica/presets |
| 7 | Tratamento microfone completo |
| 8 | Soundboard/pads |
| 9 | Alteração de voz, pitch/formantes, ambientes |
| 10 | Surround e HRTF, shifter, canais reais |
| 11 | Gravação multifaixa, perfis, automação |
| 12 | Otimização, compatibilidade, documentação, instalador, desinstalador e distribuição |

Infraestrutura de idioma na primeira UI pós-F3. Design profissional após núcleo funcional. Nenhum recurso avançado pode ser declarado real sem backend e validação correspondentes. F3 é barreira antes da expansão em massa; se bloqueada, documentar alternativa/decisão, não ignorar.

## 8. Fotografia inicial de transferência — 18/11/2026

**Não é estado vivo. Consultar HEAD, PROJECT_STATUS, REVIEW e NEXT_TASK a cada retomada.**

- Versão v0.1.0 Alpha experimental; sem instalador/release pública. Fase 0 documentada, Fase 1 base implementada com aceitação física parcial. Fase 2 em curso; não declarar encerrada.
- F1-I1–I6, MIC/METER e identidade oficial: seleção/volume/mute/balanço, sessões e medidores Windows reais, hotplug, preferências JSON/backup, teste curto até 10 s em memória e reprodução explícita. EQ-PREVIEW-1 e EQ-PROFILE-1: EQ real de 10 bandas, preamp, bypass, A/B e perfis locais, **somente reprodução da gravação curta dentro do Wave Studio**. Escuta A/B relatada. CHECKUP-1 preserva cleanup seguro e fechamento.
- F2-I1–I5: PCM16/float32, SPSC, adaptação mono/LR, FIR nominal (44,1↔48, 96→48 e identidades suportadas), adaptativo e clock/drift em simulação, contratos WASAPI isolados. Não reescrever base. F2-I6A (`feb7001`): ponte pública IAudioClock/HRESULT, preparo e liberação nativa sem Start; 603 testes por configuração.
- F2-I6B1: transporte nativo opt-in e sempre SILENT, guard compartilhado, owners MTA, priming, stop/release e proteção de recursos. Provas 1–3 não aprovadas: descontinuidade inicial e rejeições de clock; causa física não comprovada. FIX, CLOCK-DIAG e CLOCK-WINDOW corrigiram política/diagnóstico dentro de limites. `f72304e` é última implementação funcional antes da prova 4; tolerância por janela exclusiva da captura SILENT, render/estimador estritos.
- Última regressão relatada: 737 aprovados Debug e 737 Release, 214 direcionados WASAPI, dois Explicit excluídos; SDK 10.0.404, builds x64 sem erros/avisos. Falhas intermitentes anteriores de alocação (EQ Debug 6.040 bytes, resampler nominal Release 3.992 bytes) passaram isoladas e em repetição sem alteração; causa indeterminada. Não reduzir asserções, investigar se recorrentes.
- `54e3c1c`: prova 4 registrada, somente documentação; uma tentativa física aprovada, código 0, temporizador 5 s concluído, QPC 4,9997441 s, 499 pacotes contíguos/239.520 frames aceitos, descarte inicial 1/480; 29 janelas clock, 298 de pacotes, seis provisórias, corroboração 0,235279 frame dentro do limite 1.158,792512. Start/Stop/release confirmados em ambos, saída SILENT, nenhuma voz armazenada segundo relato; zero drops, 960 frames underrun interno. Não prova drift físico, estabilidade longa, monitoramento audível, latência fim a fim ou integração externa. JSON bruto local ignorado pelo Git, não revisado independentemente.
- Sete casos manuais A01/A02/A03/A05/A09/A11/A12 pós-formatação aprovados por relato; outros casos/subcasos e aceitação integral Alpha pendentes. Windows 11 x64 prioritário ainda sem validação física; Windows 10 experimental.
- `80a0f59`: revisão formal da prova 4 e NEXT_TASK COMENTARIOS-PTBR. Tarefa apenas textual: inventário/tradução dos comentários próprios, equivalência sintática/tokens/strings, regressões completas, sem Explicit. Ainda pendente no instante de criação deste guia; conferir se Codex já executou.
- Sequência imediata: revisar COMENTARIOS-PTBR após publicação; preparar **F2-I6B2** (checkpoint nativo coerente, média temporal, granularidade, sincronização e controle de drift físico; considerar underruns); depois concluir critérios F2, monitoramento opt-in e validações pertinentes antes da prova externa F3. Detalhar B2 em tarefa própria, não presumir autorização de Start. Internacionalização na primeira UI pós-F3.
- Não repetir prova 4 nem os sete casos já aprovados sem regressão relevante/justificativa. Não inferir causa das falhas anteriores nem tratar tolerância de fase como drift comprovado.

## 9. Engenharia, testes e segurança

Stack C#/.NET 10 LTS, WinForms, x64, NAudio, Core Audio/WASAPI, JSON versionado, xUnit v3, Git. SDK conforme global.json, dependências lockadas. Usar C++/WDK apenas se necessário para caminho de integração validado. Consultar documentação oficial e licenças ao decidir APIs. Windows 11 prioritário; validar Windows 10 separadamente, ARM64 futuro.

- Separar UI, acesso a áudio e DSP. Callbacks sem I/O bloqueante, rede, alocação recorrente ou locks desnecessários. Preservar ownership, geração, cancelamento, desconexão, quiescência e descarte. Não encerrar à força recurso/worker em estado incerto; manter guard e evidência.
- Volume Windows, ganho digital e master do mixer são conceitos distintos. Níveis iniciais neutros, efeitos e monitoramento desligados; headset em nível baixo quando autorizado, proteção contra feedback/clipping e parada segura. Não capturar, gravar ou reproduzir automaticamente.
- WASAPI loopback não substitui áudio original. DSP externo exige caminho real validado (APO/virtual endpoint ou equivalente). Não confundir prova silenciosa, EQ curto, simulação ou consulta de latência com monitoramento audível, latência acústica ou integração externa.
- Sem driver próprio nos controles básicos. Sem APIs privadas/injeção em jogos, drivers proprietários copiados, desativação de Secure Boot/assinatura/integridade, elevação ou instalação sem autorização. Não modificar Windows ou volumes inadvertidamente.
- Restore locked/auditoria NuGet, builds Debug/Release x64, testes direcionados e regressões pertinentes conforme checkpoint. Runner .NET direto, watchdog versionado, política Restricted preservada, opt-out de telemetria de testes conforme procedimento atual. Registrar comandos, versões, números, duração, falhas iniciais e diagnósticos; não inventar resultados ou mascarar falhas.
- Testes Explicit separados, não executar junto à regressão comum. Nova autorização específica a cada prova ativa, condições imediatas (headset conectado, app fechado normalmente, sem chamadas/outros runners), endpoints exatos ativos e únicos por direção, sem fallback. Opt-in apenas no processo. Não repetir automaticamente se falhar; preservar evidências e parar. A duração, nível e SILENT/audível são definidos em tarefa específica. Aprovação anterior não autoriza prova nova.
- Não guardar PCM, voz, nomes/IDs de endpoints ou segredos em relatórios públicos/versionados. Artefatos privados/ignorados continuam locais; respeitar requisitos e política de privacidade. Não diminuir asserções, tolerâncias ou proteções para obter aprovação.

## 10. Ambiente e recuperação

Projeto privado, VS Code/Codex e Git no Windows. Pasta oficial registrada em RECOVERY_AUDIT; verificar raiz real antes de qualquer operação, não pressupor caminho após formatação. Manter uma pasta ativa e GitHub como backup do código versionado. GitHub não contém automaticamente arquivos ignorados, dados locais, preferências, credenciais, logs de provas ou histórico integral do Codex.

Nunca reset/clean/force push, sobrescrever trabalho local, stash automático, excluir backups/ZIP/pastas ou mover projeto sem autorização. Se local divergir, preservar e reportar; sincronizar por fast-forward seguro somente. Se necessário, inventariar arquivos únicos, confirmar backup e plano de recuperação antes de mudança. Não reinstalar SDK, drivers ou ferramentas desnecessariamente. Não expor credenciais. RECOVERY_AUDIT contém contexto pós-formatação e procedimentos detalhados.

## 11. Manutenção deste próprio guia (obrigatória)

Este arquivo é **exclusivamente para transferência entre chats do ChatGPT**; não é leitura de rotina nem tarefa do Codex. O ChatGPT deve mantê-lo atualizado **a cada novo prompt de andamento/checkpoint**, inclusive revisões, bloqueios e mudanças de decisão, no mesmo commit documental de REVIEW/NEXT_TASK sempre que possível. Registrar uma nova entrada na seção de passagens e preservar as anteriores. Não deixar informações importantes apenas em conversas. Registrar decisões permanentes também em AGENTS, REQUIREMENTS ou plano mestre conforme pertinente. O Codex não modifica este guia. Antes de encerrar um chat por limite ou trocar de chat, acrescentar uma entrada datada na seção de passagem abaixo com:

- HEAD atual e último commit funcional; tarefa/estado e última revisão;
- testes e validações aprovados, pendências, riscos/bloqueios e limites de evidência;
- autorizações concedidas/consumidas, novas decisões do proprietário;
- próxima ação exata, esforço e referência a NEXT_TASK;
- alterações locais/artefatos não versionados conhecidos (sem dados sensíveis).

Se o limite ocorrer sem aviso, o próximo chat deve reconstruir a passagem usando HEAD, PROJECT_STATUS, REVIEW e NEXT_TASK. Não depender de atualização manual deste guia para saber o estado. Não substituir o histórico de REVIEW/CHANGELOG; preservar a trilha de decisões. Não prometer acesso integral à conversa anterior ou garantia absoluta contra erros: tornar cada etapa rastreável, com critérios de aceitação e revisão antes da próxima.

### Registro de passagens

| Data | Base e ponto de retomada | Próxima tarefa |
| --- | --- | --- |
| 18/11/2026 | WaveStudio P1 → novo chat; prova 4 em 54e3c1c, revisão e tarefa em 80a0f59; sem B2/F3 | COMENTARIOS-PTBR, Alto; revisar resultado antes de B2 |
| 18/11/2026 | COMENTARIOS-PTBR aprovado em feaf15a; 125 arquivos examinados, 74 traduzidos, 209 comentários, 737/737 regressões comuns sem falhas, 2 Explicit excluídos por configuração; prova 4 preservada. Revisão estática, logs locais não inspecionados. Guia atualizado a cada prompt por decisão do proprietário. | F2-I6B2A-CHECKPOINT, Extra alto; fundação de ledger e checkpoint coerente com fakes, sem Start físico ou ppm ativo; revisar antes de B2B/F3 |
| 18/11/2026 | B2A aprovado em b3504bd; 758/758 comuns (21 novos), 235 WASAPI Debug, 19 diário Release, 2 Explicit excluídos, 16 KiB e zero alocação recorrente em ensaio sintético. Ledger por versões com cortes coerentes e média intervalar; somente fakes, sem prova física; logs locais não verificados. Proprietário sinalizou demora da fase inicial, estratégia geral acelerada ainda não aprovada. | F2-I6B2B-DRIFT, Extra alto: agrupar evidência de clock, ocupação, controlador em fakes e preparar prova SILENT específica, sem executá-la; revisar antes de Start, monitoramento e F3 |
| 18/11/2026 | B2B em 558a431 aprovado em simulação; 780/780 finais seed 20261114, 22 novos, 311 direcionados, 3 Explicit excluídos. Três Release seed 20261113 falharam por alocações intermitentes (diário, adaptativo, EQ, nominal), causa não determinada; investigar antes de processamento contínuo/audível. 45 s virtuais +300 ppm: desvio médio 536,98→46,02 frames. Hardware ainda nominal, sem prova B2. | F2-I6B2B-DIAG-PROOF, Alto: uma coleta SILENT 10 s nominal somente após autorização e confirmação imediata, análise offline na mesma etapa, sem código novo/repetição B1; revisar evidências antes de correção física ou F3 |

## 12. Prompt para abrir o próximo chat ChatGPT

```text
Este é o novo chat do projeto WaveStudio. Acesse o repositório privado
Wave-Universal-Studio/main pelo GitHub e leia CHAT_CONTINUITY.md
integralmente. Siga o checklist de retomada, confira o HEAD atual,
leia os documentos e revise o último resultado do Codex.
Recupere o contexto anterior que estiver disponível, sem presumir
leitura integral. Identifique a tarefa vigente, dependências e
pendências. Continue o fluxo ChatGPT → REVIEW/NEXT_TASK no GitHub →
prompt curto ao Codex. Não pule etapas nem repita provas aprovadas.
Informe o que verificou e qualquer limitação antes de avançar.
```
