# test_for_cuckoo

## ModularMech(モジュラー機体ゲーム v1)

小型ロボットのパーツ換装をコアにした Unity 6 プロジェクト。戦闘は含まず、
「パーツを組み替える」と「パーツ構成に応じて挙動が変わる」の2点のみを対象とする。

- 設計ドキュメント: [docs/modular-mech-design-v1.md](docs/modular-mech-design-v1.md)
- 実装規約と裁定: [ModularMech/CLAUDE.md](ModularMech/CLAUDE.md)
- セットアップ手順: [ModularMech/README.md](ModularMech/README.md)

Unity で開くときは、リポジトリ全体ではなく `ModularMech/` フォルダをプロジェクトとして開くこと。

### 開発用のエージェント定義

`.claude/agents/` に、このプロジェクト用のエージェントを定義してある。

| エージェント | モデル | 役割 |
|---|---|---|
| `mech-implementer` | Opus | 実装。設計原則の逸脱禁止事項を保持する |
| `mech-tester` | Sonnet | EditMode テスト。境界値と手計算による期待値の導出 |
| `mech-reviewer` | Opus | レビュー(指摘のみ、コードは書き換えない) |
| `mech-advisor` | Opus | Unity ワークフロー上の判断材料(コードは書かない) |

### 静的チェック

この環境には .NET / Unity が無いためコンパイル検証ができない。
最低限の機械的チェックとして以下を用意している(コンパイラの代わりにはならない)。

```
python3 Tools/check_sources.py --root ModularMech
```
