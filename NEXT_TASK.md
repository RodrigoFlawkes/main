# Wave Studio — próxima etapa

ID: F2-I6B2B-DIAG-PROOF — coleta física diagnóstica nominal, SILENT, tentativa única
Base funcional: 558a43165f391fdd66153d36304206b568a21479. B2B aprovado apenas em simulação; prova 4 preservada. Esforço Alto. Este documento não autoriza Start; autorização expressa e confirmação imediata do proprietário são obrigatórias. Não executar se qualquer pré-condição faltar.

## Preparação sem áudio

Sincronizar por fast-forward seguro, verificar raiz oficial, HEAD/origin/main e árvore limpa, preservar alterações e artefatos. Ler AGENTS, REVIEW, PROJECT_STATUS, NEXT_TASK e seção B2B de docs/WASAPI_BACKEND_PLAN. Conferir SDK, binário Release x64 correspondente exatamente à base funcional (recompilar Release se necessário, sem mudança de código; restore locked auditado se necessário). Confirmar que commits posteriores à base são apenas documentação. Não repetir regressões comuns, provas anteriores nem testes manuais já aprovados nesta coleta.

Conferir descoberta de exatamente um método Explicit dedicado, usando o apphost/runner .NET direto com filtro completo: WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedEndpointsCollectSilentDriftEvidenceAtNominalRatio. Não executar os demais Explicit. Preservar política Restricted; não elevar privilégios nem modificar Windows.

Antes de Start, obter confirmação imediata do proprietário: headset conectado, Wave Studio fechado normalmente, ausência de chamadas, gravações e outros testes/runners, sete casos manuais já aprovados ainda aplicáveis, autorização de uma captura de 10 s em memória com render sempre SILENT e razão nominal, sem armazenamento de voz. Conferir endpoints de captura e reprodução ativos, nomes exatos e únicos por direção, formato exato, sem fallback. Usar o par anteriormente aprovado somente se reconfirmado; não presumir conexão ou trocar dispositivo automaticamente. Conferir ausência de app e outros runners localmente. Se bloqueado, registrar motivo e parar sem Start.

## Única execução autorizável

Somente após autorização específica e pré-condições completas, definir no processo do runner (não globalmente) as quatro variáveis:
- WAVESTUDIO_DRIFT_DIAGNOSTIC_START_AUTHORIZED=1
- WAVESTUDIO_DRIFT_DIAGNOSTIC_PREREQUISITES_CONFIRMED=1
- WAVESTUDIO_DRIFT_DIAGNOSTIC_CAPTURE_NAME (nome exato verificado)
- WAVESTUDIO_DRIFT_DIAGNOSTIC_HEADSET_NAME (nome exato verificado)

Executar exatamente o método acima com --explicit only --filter-method e duração configurada de dez segundos, usando watchdog/timeout seguro conforme protocolo existente e runner Release x64. Nenhuma correção física, nenhum áudio audível, encoder ou PCM persistido. Não mudar limites, code/asserções, volumes, configurações, drivers ou políticas. Não repetir automaticamente se falhar, inclusive se faltar evidência. Preservar guard/recursos em falhas de cleanup conforme contrato; não forçar liberação ou terminar processo de áudio de terceiros.

## Critérios da coleta e análise na mesma etapa

Registrar exit code, duração do runner e tempo QPC observado, DurationReached, TransportObserved, CaptureTiming.Confirmed, continuidade igual aos pacotes aceitos, Start/Stop e Release dos dois papéis, ausência de buffers em voo/recursos retidos. Exigir CorrectionEnabled=false, TrackingUpdates=0 e RequestedPpm nulo/AppliedPpm zero em toda a série disponível. Exigir ≥60 cortes coerentes, ≥10 janelas estimáveis com pelo menos 4 s entre primeira e última. Distinguir coleta aprovada de sincronização física validada.

Analisar offline o JSON original, sem nova execução. Relatar contagem de amostras e estados/rejeições, frescor, épocas/provisórias e cobertura, ocupação min/max e média intervalar, priming endpoint/FIFO, render pré-ticket e warmup, drops/underruns e contabilidade (não extrapolar cortes para terminal sem evidência), HRESULTs, limpeza. Para todas as janelas elegíveis, calcular distribuição das larguras e centros/limites ppm, estabilidade e sinal, comparar clocks/pacotes e evidenciar sensibilidade à hipótese de erro por ponto. Explicar se dados sustentam ou não precisão repetível compatível com largura ≤200 ppm; não reduzir orçamento de erro apenas para passar, não tomar ausência de rejeição nominal por precisão de drift, não afirmar causa das falhas antigas. Uma janela larga não invalida coleta bem-sucedida. Se coleta falhar, identificar critério real e preservar dados, sem novo Start. Se evidência insuficiente, propor caminho mínimo posterior (melhor observação/rota nominal limitada segura, conforme requisitos), sem implementação nesta tarefa.

## Saída

Atualizar PROJECT_STATUS, CHANGELOG e WASAPI_BACKEND_PLAN com fatos, comandos, resultado e limitações. Preservar JSON numérico em artifacts/wasapi-drift-diagnostic ignorado pelo Git; não versionar nomes/IDs de dispositivos, áudio, PCM ou dados sensíveis. Informar caminho dos logs, commit funcional usado, estado local/remoto. Publicar somente documentação se autorizado, sem alterações de código, sem force push. Não alterar REVIEW, NEXT_TASK, CHAT_CONTINUITY. Encerrar para revisão.

Pendência separada registrada: regressões Release seed 20261113 intermitentes (3 falhas de suíte; diário/adaptativo/EQ/nominal). Não repetir suítes para tentar obter aprovação nesta coleta física, não enfraquecer asserções. Investigar de forma direcionada na próxima implementação pertinente antes de monitoramento contínuo/audível; não atribuir causa sem evidência. Fase 2, correção física, monitoramento e F3 continuam condicionados à revisão. Não iniciar efeitos/design/UI ou instalação. Preservar todos os requisitos e Config → Idioma na primeira UI pós-F3.
