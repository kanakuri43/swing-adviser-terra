# 実運用開始チュートリアル

この手順は、個人利用の判断支援ツールとして本アプリを開始するためのものです。候補、保有再評価、AIチェックはすべて参考情報です。アプリは証券会社へ注文を送らず、約定を自動登録しません。

## 0. 運用前に確認すること

- 取引は生活資金と切り離した余裕資金の範囲に限定します。
- 実際の注文と約定確認は証券会社側で行います。アプリの「約定を手入力」は、証券会社の約定通知を確認した後の記録操作です。
- 初回更新は、評価日から原則5年の履歴が不足する銘柄だけを取得するため、時間がかかることがあります。途中で画面を閉じず、失敗件数と理由を確認します。
- 株価の取得元はYahoo Financeの非公式APIです。取得不能、レート制限、仕様変更は起こり得ます。失敗を成功扱いにせず、更新結果を確認してください。

## 1. 初回セットアップ

### 1-1. 実行場所を決める

Releaseビルドの実行ファイルは次です。

```text
C:\Users\su\source\repos\swing-adviser-terra\src\SwingAdviser.Presentation\bin\Release\net8.0-windows\SwingAdviser.Presentation.exe
```

データベースは、このexeと同じフォルダに `swing-adviser.db` として保存されます。書き込み権限のある場所で実行してください。保存先は暗黙に別フォルダへ切り替わりません。

### 1-2. JPXの取得元を準備する

既定では、同じPCで利用している `stock-simulator-codex` と同じJPX上場銘柄XLSを直接取得します。追加の設定なしで使えます。

| 用途 | 既定の取得先 | 上書きが必要な場合 |
| --- | --- | --- |
| 上場銘柄一覧 | JPX公式 `data_j.xlsx` | プロキシ・ミラー等を使うときだけ `SWING_ADVISER_JPX_LISTED_ISSUES_URL` |
| 制度信用・貸借一覧 | JPX公式一覧HTML | 別の公式取得先へ変えるときだけ `SWING_ADVISER_JPX_MARGIN_ISSUES_URL` |

上場銘柄一覧の取得処理はXLS/CSV/TSV/XLSXを受け付けます。既定のJPX XLSは、既存のStock Simulatorと同じ形式・URLです。

既定の取得先を上書きする場合は、次の形式を使います。

| 用途 | 設定値 | 受け付ける形式 |
| --- | --- | --- |
| 上場銘柄一覧 | `SWING_ADVISER_JPX_LISTED_ISSUES_URL` | 直接取得できるCSV/TSV/XLS/XLSX。少なくともコード、銘柄名、市場商品区分の列が必要 |
| 制度信用・貸借一覧 | `SWING_ADVISER_JPX_MARGIN_ISSUES_URL` | JPXの一覧HTML |

上場銘柄一覧は[JPX 東証上場銘柄一覧](https://www.jpx.co.jp/markets/statistics-equities/misc/01.html)で公開されています。既定のXLS URLを使う場合は設定不要です。

信用・貸借一覧には、[JPX 制度信用・貸借選定銘柄一覧](https://www.jpx.co.jp/listing/others/margin/index.html)のURLを設定できます。売建可否はテクニカルなShort候補とは別に確認し、証券会社で実際に売建可能かを必ず確認してください。

JPXのファイル形式・URLが変わった場合は、更新を繰り返さず停止してください。画面の失敗件数と更新履歴を確認し、取得アダプターを修正・検証してから再開します。

### 1-3. PowerShellで設定して起動する

通常は、PowerShellからそのまま起動できます。

```powershell
& "C:\Users\su\source\repos\swing-adviser-terra\src\SwingAdviser.Presentation\bin\Release\net8.0-windows\SwingAdviser.Presentation.exe"
```

取得先を上書きする場合だけ、PowerShellセッションに設定します。

```powershell
$env:SWING_ADVISER_JPX_LISTED_ISSUES_URL = "https://example.invalid/jpx-listed-issues.xlsx"
$env:SWING_ADVISER_JPX_MARGIN_ISSUES_URL = "https://example.invalid/jpx-margin.html"
& "C:\Users\su\source\repos\swing-adviser-terra\src\SwingAdviser.Presentation\bin\Release\net8.0-windows\SwingAdviser.Presentation.exe"
```

`example.invalid` は例です。実在する、直接ダウンロード可能なURLに必ず置き換えます。設定値をGitへコミットしたり、共有メモへ秘密情報と一緒に保存したりしません。

## 2. AIチェックの既定動作

AIチェックは既定で自動実行されます。Codex CLIは、Stock Simulatorと同じ順番（npmの標準配置、PATH上の`codex.exe`）で自動検出します。

日次更新後、Long/Shortごとのスコア上位3件を永続キューへ入れます。AIの完了を日次分析の完了条件にはせず、AI失敗・timeout・情報不足でもテクニカル候補は残ります。AI Verdictは売買推奨ではありません。

自動投入を一時的に止める場合だけ、起動前に次を設定します。

```powershell
$env:SWING_ADVISER_AI_AUTO_ENABLED = "false"
```

検出できない場所へCodex CLIを置いた場合だけ、`SWING_ADVISER_CODEX_PATH` で実行ファイルを明示指定します。

## 3. 初回の日次分析更新

1. アプリを起動します。
2. 画面上部の「日次分析を更新」を押します。
3. 進捗カードで、実行中のステップ名、完了ステップ数、銘柄取得件数、成功数・失敗数、経過時間を確認します。「外部データを更新」では評価日の確定足と分析窓がキャッシュにある銘柄は通信せず、不足分だけを取得します。中断して同じ評価日でやり直した場合は、期限内で保存済みrevisionと整合する成功分だけを再利用し、失敗・中断・期限切れ・訂正検知分だけを取得し直します。ネットワーク待機中はバーが動き、詳細に待機中の取得元と経過時間を表示します。
4. 終了後、「候補」「保有」「履歴」を順に確認します。

AIチェックが継続中の場合は、AIカードの対象数・成功数・実行中数・待機数を5秒ごとに更新します。手動約定の保存や、起動時・更新後の表示再読込中にも実行中の表示が出ます。

更新ボタンは、外部データ取得、point-in-time検証、テクニカル分析、候補生成、保有再評価、保存、必要時のAIキュー投入を実行します。更新中は中止できますが、中止してもそれまでに保存された取得ログ・分析結果は監査用に残ります。

判定基準日は、取得直後の当日足を確定足として扱わないため、JSTの直近平日です。祝日などで確定足が得られない場合、候補はfail-closedで除外されます。これはデータ不足を条件不一致や成功として扱わないための動作です。

### 初回更新で確認する項目

- `失敗 0件` とは限りません。失敗があれば、対象・理由を確認してから判断します。
- 候補が0件でも、エラーではありません。市場条件に合う候補がない、履歴が不足、またはpoint-in-time検証を通過しなかった可能性があります。
- 最新確定終値が表示され、候補の判定日と矛盾しないことを確認します。
- 保有がある場合は、決済判定、損切候補、利確候補、期限、コスト、要照合状態を確認します。未算定・期限未確認・要照合を0円や安全と解釈しません。

## 4. 毎日の運用手順

取引日の引け後、データが確定したことを確認してから次を行います。

1. 必要ならアプリを閉じた状態でDBをバックアップします（「6. バックアップ」参照）。
2. アプリを、同じPowerShell設定で起動します。
3. 「日次分析を更新」を実行し、進捗と失敗件数を確認します。
4. 候補は、方向、判定日、スコア、根拠、最新確定終値、AI状態を確認します。
5. AIを使う場合は、候補を選択して「AIチェック」を実行します。`情報不足`は`Neutral（中立）`とは異なります。
6. 実際に取引するかは証券会社画面と自分のルールで別途判断します。
7. 証券会社で約定した後だけ、アプリの「約定を手入力」から日時・価格・株数を入力し、確認画面で内容を確認して保存します。
8. 部分決済では、どのlotへ充当するかを利用者が明示して登録します。FIFOなどを推測させません。

## 5. 画面の読み方

### 候補

- `Long/Short` は分析上の方向であり、発注指示ではありません。
- `AI状態`は未実行、待機中、実行中、成功、失敗、timeout、情報不足、キャンセル、旧結果を区別します。
- `候補方向との関係`はAI Verdictとテクニカル候補との整合表示です。利益や実行可能性を保証しません。

### 保有

- `決済判定`、損切候補、利確候補は判断支援です。自動決済は行いません。
- `期限未確認`、`未算定`、`企業アクション要照合`がある場合は、証券会社の契約条件・明細・コーポレートアクションを確認します。
- コスト欠損を0円と扱わず、ネット参考損益を確定損益と混同しません。

### 履歴

- 履歴には利用者が確認して登録した約定だけが残ります。
- 訂正は元の約定を削除・上書きせず、revisionとして残ります。

## 6. バックアップ

アプリを完全に終了してから、exeと同じフォルダの `swing-adviser.db` を日付付きの安全な場所へコピーします。

```powershell
$source = "C:\Users\su\source\repos\swing-adviser-terra\src\SwingAdviser.Presentation\bin\Release\net8.0-windows\swing-adviser.db"
$backupDirectory = "D:\SwingAdviserBackup"
$destination = Join-Path $backupDirectory "swing-adviser-$(Get-Date -Format yyyyMMdd-HHmmss).db"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $destination
```

復元は、アプリを閉じた状態で現在のDBを別名へ退避してから、バックアップファイルを同じ名前・同じexeフォルダへ戻します。2026-09-04にスクラッチ環境（実行ファイル・DBのコピー、本番DBとは別）でバックアップ→退避→復元→再起動の一連を実機確認済みです。マーカー行を仕込んだ状態でこの手順を通し、復元後も起動エラーなくデータが保持されることを確認しました。`journal_mode=WAL`を有効化した現在の実装でも、アプリを完全終了すればチェックポイントされて `swing-adviser.db` 単体のコピーで問題ありません（`-wal`/`-shm`の追加コピーは不要）。

## 7. よくある問題

| 状況 | まず確認すること |
| --- | --- |
| 起動直後にDB保存先エラー | exeフォルダに書き込み権限があるか。ネットワーク共有・保護フォルダを避ける |
| 更新が一部完了/失敗 | JPX URL、ネットワーク、取得元の形式変更、Yahooのrate limit/timeout。失敗を成功として扱わない |
| 候補が表示されない | 最新確定足、十分な履歴、企業アクションのpoint-in-time検証、更新失敗を確認する |
| AIが実行されない | `SWING_ADVISER_CODEX_PATH`、CLIへのログイン、AI自動投入の有効化、候補の手動選択を確認する |
| 保有の期限・コストが未算定 | 証券会社の契約条件・取引明細を確認する。推測や0円補完はしない |

## 8. 実運用開始判定

次をすべて満たすまでは、少額・期間限定の試験運用にとどめます。

- [ ] JPXの2取得元で、初回更新と翌日の再更新を確認した。
- [ ] 更新失敗時に、失敗理由を確認できた。
- [ ] 候補・保有・履歴が実データで期待どおりに表示された。
- [ ] 手動約定登録と部分決済のlot指定を、証券会社の通知を使って確認した。
- [ ] AIを使う場合、成功・失敗・timeout・情報不足の各表示を確認した。
- [ ] DBバックアップのコピーと復元を、実データを壊さない場所で確認した。

関連する設定の全一覧は [`runtime-configuration.md`](./runtime-configuration.md)、データ取得の根拠と制限は [`data-sources.md`](./data-sources.md) を参照してください。
