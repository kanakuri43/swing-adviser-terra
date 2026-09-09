# Runtime configuration

Phase 12 の「日次分析を更新」は、利用者が明示的に開始する分析処理です。注文、約定登録、自動売買は行いません。

以下は起動前に環境変数として設定します。値に秘密情報を含める場合も、リポジトリへコミットしてはいけません。

| Variable | Purpose | Default |
| --- | --- | --- |
| `SWING_ADVISER_JPX_LISTED_ISSUES_URL` | JPX上場銘柄一覧の取得先を上書きする絶対URL（CSV/TSV/XLS/XLSX） | [JPX公式 `data_j.xlsx`](https://www.jpx.co.jp/markets/statistics-equities/misc/tvdivq0000001vg2-att/data_j.xlsx) |
| `SWING_ADVISER_JPX_MARGIN_ISSUES_URL` | JPX信用・貸借銘柄一覧の取得先を上書きする絶対URL | [JPX公式一覧HTML](https://www.jpx.co.jp/listing/others/margin/index.html) |
| `SWING_ADVISER_YAHOO_BASE_URL` | Yahoo Finance APIのベースURL | `https://query1.finance.yahoo.com/` |
| `SWING_ADVISER_DATA_TIMEOUT_SECONDS` | 外部データ要求のタイムアウト秒数 | `60` |
| `SWING_ADVISER_DATA_MAX_CONCURRENCY` | Yahoo銘柄データを同時取得する上限（1〜8）。SQLiteへの保存は順番に行う | `8` |
| `SWING_ADVISER_SCAN_MAX_CONCURRENCY` | ステップ5の銘柄別指標計算を同時実行する上限（1〜8）。SQLiteの監査用保存は順番に行う | `4` |
| `SWING_ADVISER_HISTORY_LOOKBACK_YEARS` | 日次スキャンの有限履歴窓（年）。必要指標のウォームアップ期間がより長い場合はそちらを優先 | `5` |
| `SWING_ADVISER_FETCH_CHECKPOINT_VALIDITY_MINUTES` | 同一評価日の成功取得を中断再開で再利用できる時間（1〜1440分）。期限切れなら再取得する | `360` |
| `SWING_ADVISER_CODEX_PATH` | Codex CLI実行ファイルを明示指定する | npmの標準配置 → Codexデスクトップ版の`%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe`／`%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\*\codex.exe` → PATH上の`codex.exe` → `codex` |
| `SWING_ADVISER_CODEX_WORKING_DIRECTORY` | Codex CLIの作業ディレクトリ | 未設定 |
| `SWING_ADVISER_CODEX_MODEL` | Codex CLIに渡すモデル名 | 未設定 |
| `SWING_ADVISER_CODEX_ARGUMENTS` | Codex CLI追加引数。US区切り（U+001F） | 空 |
| `SWING_ADVISER_CODEX_TIMEOUT_SECONDS` | AIチェックのタイムアウト秒数 | `300` |
| `SWING_ADVISER_CODEX_MAX_CONCURRENCY` | AIチェック並列数（1〜2） | `2` |
| `SWING_ADVISER_AI_AUTO_ENABLED` | 日次更新後の自動AIキュー投入を有効化するか | `true`（無効化するときだけ`false`を指定） |
| `SWING_ADVISER_AI_AUTO_TOP_COUNT` | 有効時の各方向上位候補数 | `3` |

AIの自動投入を有効にしても、AI結果は参考情報として非同期で保存・表示するだけです。約定や発注を生成しません。

日次更新は、参考プロジェクトの「今すぐスキャン」と同様にキャッシュ優先です。評価日の確定済み日足と必要な分析履歴がある銘柄は通信せず、足りない銘柄だけを分析窓（既定5年）で取得します。キャッシュ利用時は銘柄ごとの取得監査行や履歴指紋を再作成せず、更新runに集計の再利用記録を1件だけ残します。日足そのもののsource revisionは従来どおり保持されます。全銘柄のPER/PBR取得は候補抽出前の必須処理にはしません。JPX上場銘柄一覧のXLS取得とCodex CLIの検出順は、同じPCで利用している `stock-simulator-codex` の実装を流用しています。環境変数は、プロキシ・ミラー・別配置などを使う場合だけ設定します。

分析入力manifestも、選択された日足revision・企業アクションrevision・履歴窓が同一なら、再スキャン時に再利用します。分析時刻が新しくなっても選択入力が同じであることをhashで確認するため、未来データは混入しません。

Yahoo株価要求は、並列取得中でも開始を200ms間隔（毎秒最大5件）に制限する。これは参考プロジェクトと同じレート制御であり、レート制限による失敗・再試行の増加を避けるためである。初回や履歴不足時は分析窓全体を取得するが、既存履歴があり新しい評価日だけ不足する場合は直近14日を重ねた差分要求にする。中断後は、未取得・暫定・履歴不足の対象について有効な成功チェックポイントを再利用し、失敗・中断・期限切れ・保存済みrevision不一致の対象だけを再取得する。

日次更新の判定基準日は、取得直後の当日足を確定足として扱わないため、JSTの直近平日です。祝日などで確定足がない場合は、既存のpoint-in-time検証が候補を fail-closed で除外し、その理由を記録します。
