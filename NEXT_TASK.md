# Wave Studio — próxima tarefa

ID: F1-I6
Base de código revisada: 15797ee
Estado: pronta para execução mediante autorização explícita do proprietário.
Esforço recomendado do Codex: Alto.

## Fluxo
- Confirmar remoto Wave-Universal-Studio/main, branch main e git status; se limpo, sincronizar com git pull --ff-only. Se houver divergência ou alterações locais, preservar e reportar; não resetar nem forçar.
- Ler AGENTS.md, docs/REQUIREMENTS.md, docs/ARCHITECTURE.md, docs/IMPLEMENTATION_PLAN.md, docs/TEST_PLAN.md, PROJECT_STATUS.md, REVIEW.md (F1-I5) e docs/FUTURE_AUDIO_FEATURES.md como referência futura.
- Executar somente F1-I6, quando autorizado. Tratar correções da revisão. Não alterar NEXT_TASK.md ou REVIEW.md. Não iniciar Fase 2, redesign completo, efeitos ou integração VST. Não instalar drivers, elevar privilégios ou modificar configurações críticas. Commit/push somente quando autorizado.

## Objetivo: validação e executável x64 da Alpha
1. Corrigir os pontos 1–3 da revisão F1-I5 e regressões encontradas. Revisar exceções no encerramento e consistência dos estados salvos. Não ampliar funcionalidades além do escopo da Alpha.
2. Avaliar medidores contínuos por sessão conforme requisito "quando expostos". A API já foi consultada pontualmente; medir com sessões reais e desenhar ciclo seguro por instância e visibilidade. Implementar somente se viável sem reescrita ampla, sem bloquear UI nem expor medidores falsos; caso contrário documentar bloqueio e plano posterior. Preferência durável por sessão somente se identidade inequívoca verificável; não usar PID/nome como substituto. Nenhum dos itens autoriza escrita implícita.
3. Executar restore locked, build Debug e Release x64 sem avisos, regressão dos 174 testes, novas regressões e git diff --check. Testar inicialização/fechamento, armazenamento isolado, inscrições e leituras reais. Registrar resultados, ambiente e limitações. Ensaiar estabilidade por tempo definido (sugestão 10 min) com CPU/RAM, erros, handles/recursos e recuperação, sem iniciar áudio não autorizado. Não extrapolar latência de consulta para latência de DSP.
4. Gerar publicação Release win-x64 de WaveStudio.exe por dotnet publish, preferir framework-dependent nesta etapa (explicitar runtime .NET Desktop 10 x64), diretório de saída isolado/ignorado. Validar existência e inicialização do executável publicado com --settings-file isolado. Registrar comando, caminho, dependências e tamanho; SHA-256 do artefato local se útil. Não versionar binários, configurações pessoais, logs privados ou bin/obj. Não criar instalador nem release pública. Avaliar self-contained/assinatura para etapa posterior e documentar licenças dos componentes distribuídos.
5. Produzir roteiro curto de aceitação para o proprietário, preferencialmente docs/ALPHA_ACCEPTANCE.md, com itens, condições, resultados observados e status aprovado/falhou/pendente. Separar Windows 10 experimental e Windows 11 prioritário. Instruir usar volume baixo, anotar e restaurar valores e padrões anteriores; não aplicar níveis altos, não alterar dispositivos/volume automaticamente. Ensaios físicos:
   - abrir exe, verificar seleção independente, padrão e comparação com Windows;
   - reproduzir áudio e falar para observar picos reais, sem monitoramento/feedback;
   - volume/mute/balanço em reprodução e captura (mono desabilitado), centralização, slider/teclado, mudanças externas;
   - mixer com dois apps e sessões distintas, isolamento real de volume/mute;
   - desconectar/reconectar USB, seguir padrão independente e mudança externa, suspensão/retomada;
   - salvar/reabrir preferências, restaurar mediante confirmação e recusar confirmação, verificar backup/recuperação em armazenamento temporário;
   - testar layout/rolagem, diálogos, DPI e resolução 1366x768 ou disponível; estabilidade e encerramento.
   Solicitar participação do proprietário para etapas que dependem de ação humana. Se Computer Use continuar falhando, não repetir indefinidamente nem declarar aprovação; devolver comandos e checklist curto. Não suspender/desconectar por automação ou alterar níveis sem autorização específica. A ausência de Windows 11 é bloqueio de compatibilidade, não falha presumida.
6. Atualizar PROJECT_STATUS.md, CHANGELOG.md, README, docs/TEST_PLAN.md, docs/IMPLEMENTATION_PLAN.md e arquitetura conforme resultados. Preservar requisitos originais. Registrar o que foi realmente feito e pendências. F1-I6 pode ter checkpoint técnico publicado enquanto aguarda evidências físicas, mas o estado deve permanecer "validação pendente", não Alpha funcional aceita. Corrigir falhas críticas antes de aceitar. 
7. Se autorizado publicar checkpoint, commit sugerido: test: validate and package Wave Studio alpha preview. Confirmar árvore limpa e sincronizada. Nunca criar tag final de Alpha/encerrar Fase 1 sem aprovação explícita e critérios físicos comprovados. Não iniciar Fase 2.

## Critério de saída e handoff
Relatar build/testes reais, caminho do executável Release, comando de lançamento, roteiro de teste para proprietário, falhas/limitações e SHA se publicado. Distinguir checkpoint técnico de aceitação final. Se depender de teste físico, aguardar relato do proprietário antes de concluir a validação. O próximo trabalho após a base será planejamento visual profissional e motor de áudio, com esforço Extra alto a ser recomendado quando chegar ao motor.
