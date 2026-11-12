# Wave Studio — próxima tarefa

ID: F1-I3
Base de código revisada: 44bc83a
Estado: pronta para execução mediante autorização explícita do proprietário.
Esforço recomendado do Codex: Alto.

## Fluxo
- Confirmar remoto Wave-Universal-Studio/main, branch main e git status. Se árvore limpa, sincronizar com git pull --ff-only. Se houver divergência ou alterações locais, preservar e reportar; não resetar nem forçar.
- Ler AGENTS.md, docs/REQUIREMENTS.md, docs/ARCHITECTURE.md, docs/IMPLEMENTATION_PLAN.md, docs/TEST_PLAN.md, PROJECT_STATUS.md e REVIEW.md.
- Executar somente F1-I3 quando o proprietário autorizar. Se já executada no PROJECT_STATUS.md, não repetir.
- Tratar os ajustes da revisão do Incremento 2 primeiro; não modificar NEXT_TASK.md nem REVIEW.md. Registrar decisões e resultados no status/changelog.
- Não instalar drivers, elevar privilégios ou alterar configurações críticas sem autorização. Commit/push apenas quando o proprietário autorizar explicitamente. Não iniciar Incremento 4.

## Objetivo: mixer de aplicativos do Windows
Implementar sessões reais de reprodução do endpoint selecionado, usando APIs públicas Core Audio via NAudio.Wasapi 3.1.0 e mantendo Desktop separado de Audio.

### Entrega funcional
1. Listar sessões reais, incluindo nome amigável/processo quando disponível, PID auxiliar, estado, volume 0–100% e mute independente. Identificar sons do sistema e múltiplas sessões de um mesmo aplicativo, sem agrupamento destrutivo. Não listar sessões de outros endpoints como se pertencessem ao atual.
2. Usar IAudioSessionManager2, IAudioSessionControl2 e ISimpleAudioVolume pelos wrappers verificados. Chave de sessão baseada em endpoint e identificador de instância; PID sozinho não serve. Não aplicar preferências duráveis ou roteamento neste incremento.
3. Controles reais de volume/mute por sessão, somente após gesto explícito; abertura/refresh nunca escrevem. Sincronizar mudanças externas e reconciliar criação, encerramento e troca de endpoint. Polling moderado é aceitável nesta etapa; notificações persistentes ficam no Incremento 4. Não confundir volume do endpoint com volume da sessão.
4. Serializar operações no serviço, usar snapshots sem COM na UI, validar ID/revisão da seleção e identidade de sessão imediatamente antes da escrita. Descartar respostas antigas e objetos COM com propriedade clara. Tratar processo encerrado, PID reutilizado, sessão expirada, acesso negado e ausência de metadados. Não aplicar comando atrasado em sessão diferente. Se uma chamada nativa já começou, documentar o limite do cancelamento.
5. Exibir seção/aba Mixer de aplicativos em pt-BR com lista rolável, estado vazio, nome, volume, mute, mensagens de erro e atualização. Seleção de entrada/saída e controles existentes devem continuar funcionando. Não implementar layout premium ainda; preservar usabilidade, teclado e escala de tela. Não mostrar medidores fictícios nem controles de DSP, balanço por sessão, roteamento, ganho ou Game/Chat nesta entrega.

## Verificação
- Testar limites, escrita explícita, atualizações externas sem escrita, isolamento por sessão e endpoint, múltiplas sessões do mesmo PID, sons do sistema, PID reutilizado, sessão encerrada, troca rápida de endpoint, acesso negado, descarte e regressão dos 56 testes.
- Integração real de leitura: enumerar sessões de um endpoint Windows, relatar contagens e estados reais. Ausência de sessões é estado válido. Não iniciar reprodução ou escrever volumes sem autorização; para teste físico, registrar níveis originais e restaurá-los. O proprietário poderá usar dois aplicativos e conferir o mixer do Windows. Não alegar validação visual se Computer Use continuar falhando.
- Executar dotnet restore WaveStudio.slnx --locked-mode; dotnet build WaveStudio.slnx -c Debug --no-restore; com TESTINGPLATFORM_TELEMETRY_OPTOUT=1, dotnet test --solution WaveStudio.slnx -c Debug --no-build; git diff --check. Corrigir regressões e relatar resultados reais.
- Atualizar PROJECT_STATUS.md, CHANGELOG.md, docs/ARCHITECTURE.md, docs/TEST_PLAN.md e demais documentos pertinentes. Preservar requisitos. Separar testes simulados, integrações reais e testes físicos/visuais pendentes. Verificar credenciais e artefatos ignorados.
- Se o proprietário autorizar a publicação, usar commit descritivo (sugestão: feat: implement per-application audio session mixer), push normal e confirmar sincronização. Se houver falhas relevantes, não publicar como concluído; relatar bloqueio.

## Critério de saída
Mixer compilado e com testes aprovados, sessões reais enumeradas e controles de sessão implementados, pendências físicas explicitadas, correções da revisão registradas. A aceitação física da Alpha continua pendente. Não iniciar Incremento 4; devolver resumo e SHA se publicado.
