# Wave Studio — próxima etapa

ID: VALIDATION-EQ-PROFILE-1 — teste manual A14
Base funcional: c1d4c8871bf64e9fe4dc5c65a472861f63403d54 (557 testes por configuração, prévia local republicada).
**Aguardar retorno do proprietário antes de novo incremento.** Revisão em REVIEW.md. Não repetir testes automatizados ou alterar código apenas por este checkpoint.

Abrir WaveStudio.exe da pasta artifacts/alpha-preview/win-x64 usando o mesmo arquivo de configurações nas duas aberturas; preferir arquivo temporário isolado para ensaios. Em Testar microfone: ativar EQ, 1 kHz −9 dB, entrada −3 dB, criar perfil Voz; mudar 1 kHz para −6 dB sem Salvar alterações, fechar/reabrir. Esperado: ativação mantida, −6/−3, perfil Voz, indicação de ajustes diferentes, gravação vazia, nível de teste20%, nenhum áudio automático. Escolher Padrão, recusar troca e preservar; confirmar troca, verificar neutro/desligado, reaplicar Voz (−9/−3). Testar salvar/renomear/excluir com confirmação e controles bloqueados durante atividade. Ver docs/ALPHA_ACCEPTANCE.md A14. Não alterar nível Windows para compensar EQ. Registrar só subcasos realmente testados.

Após confirmação/revisão: F2-I6A — ponte mínima de IAudioClock/HRESULT S_OK/S_FALSE da mesma sessão WASAPI, preparação sem Start, sem driver ou captura/reprodução. Definir esforço antes do prompt (Extra alto se justificado, avisar proprietário). F2-I6B e F3 posteriores; não ampliar efeitos em massa sem prova de integração externa. A01–A13 restantes, USB/suspensão e Windows11 pendentes; Windows10 experimental. Preservar qualidade e plano mestre. Nenhuma nova implementação autorizada por este arquivo.
