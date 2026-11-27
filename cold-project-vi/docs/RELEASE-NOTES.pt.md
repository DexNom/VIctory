Julia's Launcher 0.3.11: menos crashes em PCs de 16 GB, e os teus saves copiados antes de cada jogo.

- **Menos crashes quando a memória aperta.** Em PCs com 16 GB de RAM, o Windows recusava por um momento um buffer de 64 MB ao VIctory, e o jogo fechava ("no host list for a state prelude", "device removed"). As logs dos jogadores mostraram isto em RX 9070 XT, RX 6600, RX 6700 XT e RTX 4070. O VIctory liberta agora os blocos de memória vazios que guarda de reserva (até 4 GB da placa e 512 MB do PC) e tenta de novo antes de desistir.
- **Cópias dos saves.** Os teus saves são copiados antes de cada jogo (só quando mudaram; ficam os 15 mais recentes). Hardware e ID > Saves do jogo > **Restaurar uma cópia...** traz qualquer um de volta, e guarda também os saves atuais.
- O motor AMD do Belobug só é usado em placas Radeon e no máximo a 1080p: a 1440p e 4K fechava ao arrancar.

É seguro? [docs/SAFETY.md](https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md) · [Política de assinatura de código](https://github.com/felicienradio/cold-project-vi/blob/main/docs/CODE-SIGNING-POLICY.md)
