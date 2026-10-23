#!/bin/bash
# uso: ./mod/run_csx.sh cambio_tune_test
set -e
CSX=$1
G=/home/gullin/.local/share/Steam/steamapps/common/"My Car My Life"
cd /home/gullin/projetos/mcml-fixer
~/.local/bin/mcml-fixer --mod-uninstall >/dev/null 2>&1 || true
~/.local/bin/mcml-fixer --mod-install >/dev/null 2>&1
rm -f mod/t.unx mod/t_out.unx
cp "$G/assets/game.unx" mod/t.unx
python3 -c "
p='mod/t.unx'; b=bytearray(open(p,'rb').read())
b[0x4c:0x50]=(1280).to_bytes(4,'little'); b[0x50:0x54]=(720).to_bytes(4,'little')
open(p,'wb').write(b)"
timeout 400 ~/.local/share/mcml-fixer/utmt/UndertaleModCli load mod/t.unx -s mod/$CSX.csx -o mod/t_out.unx -f 2>&1 | grep -E "MARK|error CS" | head -3
cp mod/t_out.unx "$G/assets/game.unx"
~/.local/bin/mcml-fixer --native-res 1280x1024 >/dev/null 2>&1
