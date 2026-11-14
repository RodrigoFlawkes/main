# Wave Studio — próxima etapa

ID: VALIDATION-CHECKUP-1 — aceitação manual do proprietário
Base funcional: faaac358ce6f0d873b9f4c8cc0c255c4b660bcb6 (519 testes por configuração, prévia local republicada).

**Pausa de implementação até retorno do proprietário.** CHECKUP-1 concluído, revisão em REVIEW.md. Não executar novo incremento, alterar código ou repetir suítes automaticamente. Abrir o executável da pasta artifacts/alpha-preview/win-x64, mantendo pacote completo; usar preferências isoladas conforme docs/ALPHA_ACCEPTANCE.md. Fones e nível baixo, sem chamadas em curso; anotar e restaurar níveis/padrões após ensaio.

- A01/A02/A03/A12: abertura, seleção entrada/saída, pico durante fala e áudio, volume/mute e balanço somente quando suportado (mic mono indisponível é esperado).
- A11/A13: gravação até 10s, parar, ouvir original, ativar EQ, cortar 1kHz em −9dB, ouvir com EQ, comparar, reset/bypass, stop e limpar. Não aumentar nível físico para compensar headroom. EQ só afeta gravação curta, não Discord/Windows inteiro.
- A04: dois apps com áudio, volume/mute de uma sessão sem afetar a outra.
- A05/A09: troca/USB/suspensão por ação própria, observar descarte e recuperação; fechar durante gravação ou reprodução curta, abrir novamente. Se parada pendente, aguardar e tentar fechar novamente, sem forçar processo.
- A06/A07/A08: preferências, backup e recuperação apenas com arquivo temporário, reabertura, layout/DPI/rolagem/teclado conforme disponível. A10 Win11 permanece pendente se ambiente indisponível.

Registrar aprovado/falhou/pendente por caso/subcaso. Não marcar aceitação completa sem evidência. Após revisão dos resultados, preparar F2-I6A (relógio WASAPI com HRESULT S_OK/S_FALSE da mesma sessão, preparação sem Start). Não autorizar F2-I6B/stream físico contínuo, driver ou F3 neste checkpoint. Plano mestre e qualidade preservados.
