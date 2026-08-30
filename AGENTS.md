# AGENTS.md

## Purpose
日本株を対象とした Windows デスクトップの**株式売買判断支援アプリ**。
目的は、(1) 信用取引の新規候補（買い・売り）の探索、(2) 保有ポジションの損切・利確・継続保有判断の支援。
数日〜数週間のスイングトレードを想定し、基本時間軸は日足とする。
想定利用者は、生活資金とは切り離した余裕資産の範囲内で個人的に売買判断を行う個人投資家であり、機関投資家・法人・専業トレーダー向けの業務システムではない。

このアプリは注文システムでも仮想売買ゲームでもない。実際の発注は利用者が証券会社側で行う。

詳細なドメイン仕様は分割済みの docs を参照する。矛盾がある場合は本ファイル（AGENT.md）を優先する。

## Docs map
- [`docs/product-spec.md`](docs/product-spec.md) — ドメイン用語、日次更新フロー、候補一覧/保有/約定履歴の表示要件、UI/UX 方針
- [`docs/data-sources.md`](docs/data-sources.md) — 対象市場範囲、株価/信用情報/ファンダメンタル情報の取得元、日時の扱い（「どこからデータを取るか」を集約）
- [`docs/technical-analysis.md`](docs/technical-analysis.md) — テクニカル指標・シグナル・候補スコア算出、未来データ混入対策、参考実装（「データをどう分析するか」）
- [`docs/risk-management.md`](docs/risk-management.md) — 損切/利確ルール（ATR倍数・R倍率・テクニカル反転条件）
- [`docs/ai-analysis.md`](docs/ai-analysis.md) — AI チェック（Codex CLI）の調査項目・結果 schema、Codex CLI 実行設定
- [`docs/database-schema.md`](docs/database-schema.md) — SQLite業務テーブル、列、キー、revision、監査・point-in-time制約

## Non-negotiable rules
- 証券会社へ注文を送信しない。自動売買を実装しない。
- 「仮想注文」「仮想購入」「即時約定」等の文言・機能・導線を追加しない。
- BUY/SELL サイン、現在値、AI 判定から売買履歴を自動生成しない。
- 約定日時・約定価格・株数は利用者が明示的に入力/確認して登録する。
- 現在値/終値を約定価格へ自動採用しない。サイン日時を約定日時へ自動採用しない。
- 候補一覧から登録画面へ渡してよいのは、銘柄や方向などの入力補助まで。
- ボタン1回で売買成立・履歴登録まで行う UI を作らない。
- 分析結果と AI 結果は参考情報。利益保証・確実性を示す表現をしない。
- 過去時点の分析へ未来の価格・決算・ニュースを混入させない。
- 上記と矛盾する既存実装を見つけた場合は問題として報告する。

## Tech stack
- C#
- WPF
- MVVM
- Prism
- MahApps.Metro
- SQLite 3
- EF Core Migrations（DB スキーマ・マイグレーション管理）
- Windows

既存の依存関係・命名・設計を優先し、合理的理由なく別フレームワークへ置換しない。
新規 NuGet 追加前に標準ライブラリ/既存依存で解決できないか確認する。

## Architecture
- **Domain**: 銘柄、日足、企業アクション、ポジション、MarginLot、信用契約条件、信用コスト台帳、ポジション調整、約定履歴、戦略、シグナル、分析結果。WPF/Prism/SQLite/HTTP/Codex CLI を直接参照しない。
- **Application**: 株価更新、全銘柄スキャン、候補抽出、保有再評価、AI チェック、手動約定登録。
- **Infrastructure**: 株価データ、SQLite、HTTP、ファイル、Codex CLI。
- **Presentation**: WPF + MVVM + Prism。code-behind に業務ロジックを書かず、ViewModel から DB/HTTP/CLI を直接操作しない。

長時間処理は UI thread をブロックしない。I/O は原則 async/await。多重実行防止、キャンセル、進捗、失敗件数の可視化を優先する。

## SQLite
DB アクセスは Infrastructure/Repository 層へ隔離する。
SQL を ViewModel に書かない。
スキーマ変更を追跡可能にし、既存データを安易に破壊しない。
日足は銘柄+取引日の一意性を考慮する。
大量更新は transaction/batch を検討する。
分析日時、戦略、戦略バージョン/パラメータを追跡できる構造を優先する。
日足と企業アクションは取得時点の版を append-only で保持し、訂正で過去版を上書きしない。
分析入力manifest/hash、使用したデータrevision、企業アクションrevision、指標エンジン版、完全な戦略パラメータスナップショットを凍結し、保存済み判定を再現可能にする。
利用者が登録した元約定は監査原票として変更せず、分割等による現在基準換算は別のポジション調整履歴に保存する。
建玉期限・契約料率はMarginLot単位で版管理し、信用コストは見積と証券会社明細の確定額を区別したappend-only台帳にする。欠損を0円と推測しない。
AI 分析とテクニカル分析は責務を分ける。
削除より履歴・監査性を優先する。

マイグレーション方式(仮決定): EF Core Migrations を使用する。スキーマ変更は必ずマイグレーションとして追加し、既存マイグレーションを書き換えない。
ネーミング規則(仮決定): テーブル名・カラム名は snake_case とする（EF Core 側でマッピング設定する）。
具体的なテーブル定義・列構成の正は [`docs/database-schema.md`](docs/database-schema.md) とする。最初の業務スキーマは既存の空の `InitialCreate` を変更せず、追加マイグレーションとして実装する。

## Logging/errors
外部データ・AI・ネットワークは失敗する前提で設計する。
HTTP error、rate limit、timeout、invalid/missing data、CLI failure、SQLite lock、cancellation を区別可能にする。

最低限追跡:
- 株価更新日時/取得エラー
- テクニカル分析日時、戦略、判定
- AI チェック日時/エラー
- 手動約定履歴の登録・修正
- 企業アクションの取得・訂正・分析への適用、保有ポジションの換算・要照合
- 信用契約条件・期限の確認/改定、信用コストの見積/確定/訂正と出所

秘密情報をログへ出さない。失敗時に成功したように見える UI にしない。

## Configuration
環境依存値は設定へ分離する。
例: 株価データ取得元、API endpoint、API key 参照、Codex CLI path、AI timeout、並列数、戦略パラメータ、対象市場フィルタ。
戦略パラメータの正は設定とし、判定時にはデフォルトと上書きを解決した完全な設定JSONを正規化してDBへ不変スナップショット保存する。schema/strategy/algorithm version と SHA-256 hash も保持し、過去判定は現在の設定を参照し直さない。
API key 等の秘密情報をコミットしない。

実運用SQLite DBの保存先は設定値にせず、実行中EXEと同じディレクトリの `swing-adviser.db` に固定する。パスはカレントディレクトリではなく `AppContext.BaseDirectory` を基準に解決する。書き込み不可の場合は別ディレクトリへ暗黙フォールバックせず、起動時に明示的なエラーとして扱う。詳細は [`docs/database-schema.md`](docs/database-schema.md) Runtime database location 節を参照する。

## Testing
特にテストする:
- テクニカル指標
- シグナル境界値
- Long/Short、Entry/Exit
- 損切/利確/Hold
- 日付境界
- 欠損/分割等の不連続
- 分割前後で価格・株数・ATR・損切ラインの単位が揃うこと
- 将来の企業アクションを過去分析へ適用しないこと
- 日足/企業アクションの後日訂正後も保存済み判定を再現できること
- 未対応企業アクションが要照合となり、推測で再評価や売買履歴を生成しないこと
- 複数MarginLotの最短期限、期限改定、期限不明を正しく扱うこと
- 制度/一般信用のコスト規則を混同せず、逆日歩の休日跨ぎ、0円/不発生/未公表/欠損を区別すること
- 部分決済のlot充当を利用者確認なしに推測しないこと
- 売買履歴が自動生成されないこと
- AI 失敗時フォールバック
- Repository/migration

期待値を本体と同じアルゴリズムで再計算するだけのテストは避ける。

## Build/validation
ソリューション構成に応じて変更後に原則実行する。

```powershell
dotnet restore
dotnet build
dotnet test
```

`dotnet format` は既存運用で導入済みの場合のみ使う。
失敗した build/test を隠して完了扱いにしない。

## Codex workflow
変更前:
1. リポジトリ構成、README/docs/既存指示を読む。
2. 関連コードとテストを読む。
3. 必要なら参考リポジトリを確認。
4. Non-negotiable rules との矛盾を確認。

変更中:
1. 目的を満たす最小限の変更を優先。
2. 既存設計・命名へ合わせる。
3. UI と業務ロジックを分離。
4. 重要な金融判定ロジックにはテストを追加。
5. 自動売買へ近づく機能を追加しない。

変更後:
1. build/tests。
2. warning/failure 確認。
3. 変更点、テスト結果、未解決事項を簡潔に報告。

## Currently undecided
以下は確定仕様として扱わない。

- AI 結果の詳細なsemantic schemaとそのversion
- JPX/Yahoo等の銘柄コード正規化・コード再利用ルール
- バックテストで検証する戦略・リスク・スコアの仮パラメータ

既存コード/docs/issue に根拠がなければ、変更容易な抽象化・設定値で仮決定し、その仮定を作業結果に明記する。

## Priority order
迷った場合は次の順で優先する。

1. 誤った売買操作を誘発しない
2. 自動売買と判断支援の境界を守る
3. 判定根拠を追跡できる
4. 未来データを混入させない
5. データを壊さない
6. 実装の簡潔さ
7. テスト可能
8. UI を固めない
9. 既存設計との整合
10. 拡張性

本アプリは**売買を実行するアプリではなく、利用者自身の売買判断を支援する分析・記録ツール**である。
すべての実装判断はこの原則に従う。
