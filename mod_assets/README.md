# Arte do mod

Estes dois arquivos são a arte do distribuidor usada pelo mod dentro do jogo.

| arquivo | uso | tamanho |
|---|---|---|
| `distribuidor_icon.png` | ícone 256×256 RGBA, fundo transparente — usado nos tiles do TOTORTO, no cabeçalho do painel F6 e no marcador de estoque | 51 KB |
| `distribuidor.png` | ilustração original 1152×1728 RGBA, de onde o ícone foi recortado | 1,5 MB |

## Instalação

O `mcml-fixer --mod-install` procura a arte em
`<pasta do jogo>/assets/Mod_assets/` e copia para a **raiz** de
`~/.config/My_Car_My_Life/` (o runner é sandboxed — ver BUG 7 em
`docs/BUGS.md`).

```bash
mkdir -p "<pasta do jogo>/assets/Mod_assets"
cp mod_assets/*.png "<pasta do jogo>/assets/Mod_assets/"
mcml-fixer --mod-install
```

Sem a arte o mod funciona: o desenho cai num marcador alinhado com "DIST".
