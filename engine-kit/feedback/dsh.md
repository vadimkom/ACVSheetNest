# Отзыв: dsh-v3 (ветка agent/dsh, 61dfc2f)

Проверено владельцем 2026-11-18: `run-all.ps1` по `jobs/` и `stress/`, зёрна 1–3, и весь набор тестов SheetNest.

## Итог

- Числа отчёта подтвердились: stoika 2612.6 (v2 2652.3), остальное как в v2. `stress/`: 36 из 36, самый
  долгий 18 с.
- Весь набор SheetNest: **543 из 544** (v2: 540 из 541).
- Версия v3 заменила v2 в меню SheetNest.

## Что исправить

### 1. Общий рез в SheetNest по-прежнему ноль

`MixedCommonLineRegressionFixture.FastMixedJobKeepsAtLeastOneValidatedDirectCommonEdge`:

```
MIXED: sheets=1 placed=30 edges=0 given-up=11
MIXED: direct-plan edges=13 failure=Layout reason=normal clearance conflict (source=0 at 36,250,50,767, source=1 at 46,474,52,771)
MIXED: direct-plan edges=10 failure=Layout reason=normal clearance conflict (source=0 at 36,250,42,310, source=1 at 36,431,42,917)
MIXED: direct-plan edges=1 failure=Layout reason=normal clearance conflict (source=0 at 36,250,7,501, source=0 at 58,001,14,750)
```

Ваш счётчик видит 14 кромок, SheetNest их тоже находит (до 13 в плане). Но каждый план отклоняется:
другая пара деталей стоит ближе обычного зазора. «Подстройка зазора для каждой пары» (v3), похоже, сближает
и пары, которые общий рез не делят. Правило: зазор меньше обычного допустим только на общей кромке. Все
остальные пары касаются контурами из задания, без захода внутрь.

Проверьте и на перекрытие: `check.mjs` пропускает наложения мельче 0.01, а SheetNest нет. Например, у
opus55-high наложение 0.000245 уже снимало деталь.

### 2. Плотность на `stress/`

Хуже RNest на `tiny15` (+13.7 %), `arcs18` (+10.4 %), `tiny50` (+8.9 %), `free6`, `tiny25`, `two80`, `mixed113`.
Для сравнения: opus55-high и sonnet55-high, написанные с нуля на Go, короче Sparrow на `tiny*` и `free6`.

## Как сдать

Тег `dsh-v4`.
