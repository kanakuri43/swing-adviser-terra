# Risk Management

AGENT.md の要約から分割した、保有ポジションの損切/利確ルール。
指標そのものの定義は [`technical-analysis.md`](./technical-analysis.md) を参照。
Non-negotiable rules・優先順位は AGENT.md 側が正であり、本ファイルの内容と矛盾する場合は AGENT.md を優先する。

## Stop-loss / take-profit rules
損切/利確ルール(仮決定): ボラティリティ適応型（ATR倍数）と固定 R 倍率を併用する。
- 建玉時の基準価格を `entry_basis_price`、固定ATRを `atr_basis`、方向別の損切倍率を `k` とし、Long の損切ラインは `entry_basis_price - k * atr_basis`、Short は `entry_basis_price + k * atr_basis` とする。初期値は Long `k = 3.0`、踏み上げリスクを抑える Short `k = 2.5`（いずれも仮値）。価格、ATR、ラインは必ず同じ通貨・株式単位で扱う。倍率を狭めたことを理由に株数を自動増加させない。
- 一部利確ライン: 損切幅の 1.5R(仮値)到達で、現在数量の50%を一部利確候補として表示する（自動決済はしない）。割合は `partialTakeProfitFraction = 0.50` として設定化する。
- 残りポジションの Exit 判定: 1.5R 到達後、Long は MACD デッドクロス または EMA20 割れ のいずれか成立で Exit 候補、Short は MACD ゴールデンクロス または EMA20 上抜け のいずれか成立で Exit 候補とする。いずれも未成立の間は HOLD。
- 本アプリは自動決済・自動発注を行わない。上記はあくまで判断支援表示であり、実際の決済は利用者が証券会社側で行う。

### Holding re-evaluation decision contract v1

保有再評価は銘柄単位ではなく `position` 単位で行い、その中の未決済 `MarginLot` を個別に判定してから集約する。以下を初期実装の固定契約とし、アルゴリズム識別子を `holding-risk-evaluation-v1` とする。

#### Input and evaluation bar

- 評価日を `D` とし、`D` の確定済みまたは訂正済み日足の point-in-time 調整済み `High` / `Low` / `Close` を使う。`Open` はライン到達判定に使わず、監査用入力として保持する。暫定足、無効足、未来足、PIT 未検証系列は使わない。
- OHLC、各 lot の取得単価、固定 ATR、active risk-plan leaf の stop/take-profit は、同じ通貨・同じ現在基準の株式単位でなければならない。単位を証明できない場合は価格比較を行わない。
- ある日足を lot の到達判定へ使えるのは、その lot と当該時点の risk plan が取引セッション開始以前に有効だった場合だけとする。建玉、部分決済、利用者訂正などがセッション中に有効になり、日足だけでは前後関係を復元できない日は `IntradaySequenceUnknown` とし、その足を有利・不利のどちらにも推測しない。分割等がセッション開始前に効力発生し、一組の換算が完了している場合は換算後単位で使用できる。
- `Open` position かつ現在数量が正の lot だけを対象にする。`Closed` / `Archived` position、残数量 0 の lot は判定対象外である。

#### Price-line reach boundaries

ライン一致は到達に含む。日足だけでは日中の到達順を復元できないため、終値だけで stop/take-profit 到達を判定しない。

| Side | Stop reached | 1.5R target reached | Not reached boundary |
|---|---|---|---|
| Long | `Low[D] <= stop_price` | `High[D] >= take_profit_price` | stop は `Low[D] > stop_price`、target は `High[D] < take_profit_price` |
| Short | `High[D] >= stop_price` | `Low[D] <= take_profit_price` | stop は `High[D] < stop_price`、target は `Low[D] > take_profit_price` |

`D` より前の 1.5R 到達状態は lot ごとに求める。建玉後かつ各セッション開始前に有効だった risk-plan leaf と、同じ単位へ調整した確定足を時系列で照合し、最初の到達日、価格 revision ID、risk-plan revision ID、比較した High/Low と target を証跡として `lot_evaluations_json` に保存する。到達状態は同じ risk-basis chain 内では単調に維持するが、現在値や過去結果の boolean だけから推測せず、入力 manifest の完全な足と revision 証跡から再構築できなければならない。risk basis の訂正時は新しい chain として再計算し、supersede 済み証跡を流用しない。

#### Technical reversal boundaries

テクニカル反転は、1.5R が `D` 以前の適格足で到達した lot に適用する。MACD cross と EMA20 状態はいずれも確定終値で成立するため、当日初めて 1.5R に到達した後、同じ足の終値で反転が成立した場合も `Exit` 根拠にできる。ただし lot または plan がセッション中に発効した足は前述の `IntradaySequenceUnknown` とする。

| Side | MACD reversal | EMA20 reversal |
|---|---|---|
| Long | 前日 `MACD line >= signal` かつ当日 `line < signal`（dead cross） | 当日 `Close < EMA20` |
| Short | 前日 `MACD line <= signal` かつ当日 `line > signal`（golden cross） | 当日 `Close > EMA20` |

当日値の等値は反転成立に含めない。MACD は前日が等値で当日が反対側へ strict に移動した場合は成立する。EMA20 は一度きりの cross event ではなく当日の終値状態として評価し、反対側にある間は成立を維持する。MACD と EMA20 は OR 条件なので、どちらか一方が成立すれば反転成立、両方が評価可能で不成立の場合だけ反転不成立とする。一方が不成立でも他方が欠損なら `Indeterminate` であり、`Hold` へフォールバックしない。

4.1.5ではこの3値ORをDomainの`LotHoldingRiskEvaluator`へ実装する。MACD crossとEMA20状態はそれぞれ`Matched`/`NotMatched`/`Missing`の型付き理由を持ち、一方が`Matched`なら総合反転は`Matched`、両方`NotMatched`の場合だけ`NotMatched`、それ以外は`Indeterminate`とする。過去の1.5R状態も生booleanでは渡さず、`NotReached`、exact daily-price/risk-plan revision証跡を持つ`Reached`、または`Indeterminate`として渡す。当日target到達は4.1.4のHigh/Low比較結果から得る。stop未到達でtarget到達済みかつ総合反転`Matched`ならlotの`Exit`、`NotMatched`なら`TakeProfit`、`Indeterminate`ならdecision nullとする。stop到達はtarget/反転状態が不明でも`StopLoss`を確定できる。結果は`MarginLotId`とrisk basis/plan証跡に閉じ、instrument単位の`TechnicalAnalysisResult`は生成しない。

#### Per-lot decision table and same-bar priority

`partial exit confirmed` は、利用者確認済みの有効な部分決済約定と、その lot への有効な明示 allocation revision がある状態を指す。価格到達だけでは成立しない。

| Priority | Conditions for one lot | Decision | Partial-exit status |
|---:|---|---|---|
| 1 | 当日 stop 到達 | `StopLoss` | `NotApplicable` |
| 2 | 当日 stop 未到達、`D` 以前に 1.5R 到達、当日テクニカル反転成立 | `Exit` | `NotApplicable` |
| 3 | 当日 stop 未到達、partial exit 未確認、`D` より前または当日に 1.5R 到達 | `TakeProfit` | 数量計算可能なら `Candidate`、分割不能なら `NotFeasible` |
| 4 | 当日 stop 未到達、上記以外、必要入力がすべて評価可能 | `Hold` | `NotApplicable` |

この順序により、同一足で stop と target の双方へ到達した場合は `StopLoss`、stop と反転が競合した場合も `StopLoss`、target と終値反転が競合した場合は過去の到達有無にかかわらず `Exit` とする。代表判定にかかわらず成立した全条件を保存し、target にも到達した足は lot の過去1.5R到達証跡へ含める。これは日足から不明な stop/target の先着順を利益側へ推測しないための判定表示上の優先順位であり、約定価格や実際の約定順序を表さない。

4.1.4の価格ライン判定はDomainの`LotRiskEvaluator`へ実装する。callerは評価セッション開始時刻をcutoffとして渡し、evaluatorは`effective_at_utc`と`recorded_at_utc`がともにcutoff以前のrevisionだけから、欠落・分岐のないrisk-plan chainの単一leafを選ぶ。結果は代表`ExitDecision`に加えてstop/target双方について、line種別、比較対象High/Low、比較演算子、観測価格、ライン価格、到達有無を型付き理由として返す。これにより`StopLoss`優先時も同一足のtarget到達証拠を失わない。テクニカル反転、部分利確数量、position集約は後続タスクでこのlot結果へ追加する。

一部利確候補数量は lot ごとに `floor(lot現在数量 * partialTakeProfitFraction / 売買単位) * 売買単位` で求め、決済後に同じ lot へ最低 1 売買単位が残る場合だけ `Candidate` とする。Domain の `LotPartialExitQuantityCalculator` は企業アクション後の端株を保持できる `decimal` の現在数量と整数の売買単位を受け、候補数量、決済後数量、実効割合を元の lot ID に結び付けた純粋な提案として返す。分割不能時は数量を持たない `NotFeasible` とし、全決済候補へ丸めたり、約定・lot allocation・保有数量を生成または変更したりしない。同じ position の別 lot へ数量を暗黙配分しない。既に partial exit confirmed の lot には同じ risk-basis chain の一部利確候補を繰り返し生成しない。

#### Multiple-lot aggregation

全未決済 lot が評価可能な場合だけ position の `exit_decision` を生成する。1 lot でも判定不能なら position 全体を判定不能とし、評価できた lot の暫定結果は根拠として保持しても、position の `Hold` や売買候補へ昇格させない。

評価可能な lot は `StopLoss > Exit > TakeProfit > Hold` の順に集約し、1つでも上位判定があれば position 表示をその判定にする。これは注意喚起の代表ラベルであり、全 lot の決済や暗黙の lot allocation を意味しない。対象 lot と各判定は `lot_evaluations_json` に lot ID 昇順で残す。position の一部利確候補数量は、position 判定が `TakeProfit` の場合に限り、`TakeProfit` かつ `Candidate` の lot 別数量を合計する。上位の `StopLoss` / `Exit` がある場合は position の `partial_exit_status` を `NotApplicable` とし、下位候補は lot 別根拠にだけ残す。

#### Fail-closed outcomes

`Hold` は「必要入力をすべて評価した結果、上位条件が成立しなかった」場合にだけ使用する。判定不能を `Hold`、既知 0、または直近の成功結果へ変換しない。

| Condition | `evaluation_outcome` | Expected result |
|---|---|---|
| 全 lot の入力と判定が完全 | `Evaluated` | 上記優先順位による非 null の `exit_decision` |
| 必要な前日値または建玉後の適格足数が不足 | `InsufficientHistory` | `exit_decision = null` |
| 建玉後の取引日列に欠損があり、過去 1.5R 到達を否定できない | `HistoryIncomplete` | `exit_decision = null` |
| OHLC、指標、価格線、通貨・株式単位、revision graph が不正 | `InvalidData` | `exit_decision = null` |
| 入力系列または revision 選択が point-in-time 未検証 | `PointInTimeUnverified` | `exit_decision = null` |
| position が `Required` / `InProgress`、未対応企業アクション、訂正依存未解消 | `ReconciliationRequired` | 再評価を停止し `exit_decision = null` |
| risk basis、active risk plan、約定・allocation leaf など必須 position 入力が欠損 | `IncompletePositionData` | `exit_decision = null` |
| 同一日足内で建玉・plan 発効と価格到達の前後を決められない | `IntradaySequenceUnknown` | `exit_decision = null` |
| 予期しない計算失敗 | `Failed` | `exit_decision = null`、sanitized した診断を保存 |

判定不能 outcome でも、再構築可能な入力 manifest と lot ごとの reason code を保存する。直近の成功済み評価を画面に併記する場合は、今回の判定ではない `Stale` な参考値と明示する。再評価は `trade_executions`、lot allocation、position 数量、risk-plan revision を生成・変更しない。

### Partial take-profit state transition
1.5Rへの価格到達は候補表示だけを発生させ、ポジション数量や損切候補を変更しない。利用者が実際の部分決済について約定日時・価格・数量を明示登録し、残数量が確定した後にだけ、残ポジションの損切候補を現在基準の `entry_basis_price` へ移す。初期損切候補は監査用に残し、新しい stop-plan revision を追記する。

新しい損切候補は Long なら `max(従来候補, entry_basis_price)`、Short なら `min(従来候補, entry_basis_price)` とし、従来より不利な方向へ緩めない。これは「建値候補（コスト未調整）」であり、手数料、金利、貸株料、逆日歩、配当相当額、スリッページを含む損益ゼロを保証しない旨をUIに明示する。

4.1.7ではこの遷移をDomainの`PartialExitBreakevenPlanFactory`（`partial-exit-breakeven-plan-factory-v1`）へ実装する。利用者確認済みCloseのcurrent effective revisionと、そのexact revisionを参照する同一lotのunsuperseded `Effective` allocationがあり、今回のallocation直前数量より決済数量が小さい場合だけ、active risk-plan leafを直接supersedeする`PartialExitBreakeven` revisionを手動Close登録transaction内で追記する。新revisionのeffective時刻はClose約定時刻、recorded時刻はClose・allocation・旧planの全証跡以後とし、triggerにはlogical execution IDとexact allocation revision IDを保存する。全決済、価格到達だけ、別lot、superseded/voided Close、欠落・分岐したbasis/planでは追記しない。旧plan、約定、allocation、保有数量は上書きしない。

候補数量は `floor(現在数量 * 0.50 / 売買単位) * 売買単位` とし、残りも最低1売買単位を満たす場合だけ提示する。厳密に50%へできない場合は実効割合を併記し、分割不能なら `PartialExitNotFeasible` とする。全決済候補へ暗黙変換しない。

### ATR calculation and reference date
ATR14 は [`data-sources.md`](./data-sources.md) の同一版の point-in-time 調整済み High/Low/Close を使い、Wilder 方式で計算する。

```text
TR[t] = max(High[t] - Low[t], abs(High[t] - Close[t-1]), abs(Low[t] - Close[t-1]))
initial ATR14 = first 14 TR values' SMA
ATR14[t] = (ATR14[t-1] * 13 + TR[t]) / 14
```

先頭日足の TR は `High - Low` とする。C# `decimal` で中間丸めを行わず、表示時だけ丸める。

- 候補に紐づく建玉は、その候補の `EvaluationBarDate` のATRを使う。
- 候補に紐づかない手動建玉は、約定日時より前に確定していた直近取引日のATRを使う。後から登録した場合でも約定当日以後の日足を遡及利用しない。
- 建玉時に `atr_reference_bar_date`、ATR値、期間、方式、入力データ版、倍率、損切幅 `R` を固定する。日次のATR再計算で既存の基準値・ラインを上書きしない。現在ATRを表示する場合は参考情報として固定ATRと区別する。

初期risk basis生成時は、entry priceと固定ATRの双方へ、instrument、currency、分割・併合による1株あたり価格単位を表す`price_unit_basis_sha256`を付ける。単位hashはschema version、instrument ID、currency、およびanalysis input manifestへ凍結されたcorporate-action set hashから生成する。corporate-action setは生成時点までに適用したsplit/consolidation revision ID・比率を正規化して含み、現金配当は株式単位を変えない。entry priceとATRのcurrencyまたは単位hashが一致しない場合は生成しない。`risk_basis_snapshots.content_sha256`にはこのcurrencyと単位hashも含める。

保存時のATR provenanceはcallerから受け取らない。候補由来の新規建はexact candidateに紐づく評価日ATR14を使用する。候補なしの手動建玉は、JST約定日より前の確定足のうち、analysis runの分析・記録cutoffとmanifestのavailable/recorded cutoffがいずれも約定時刻より前で、run/manifestがpoint-in-time verifiedの最新ATR14を使用する。opening revision、candidate（存在する場合）、analysis input manifest、strategy parameter snapshot、およびmanifestのcorporate-action set hashをrisk basisへ凍結し、いずれかが不整合・欠損なら新規建全体をfail-closedにする。

`initial-risk-plan-factory-v1`は利用者確認済みOpen executionのexact effective revision、そこから作成されたMarginLot、そのlotが属するPositionを一組で受け付ける。方向をcaller入力から推測せず`Position.Side`から決め、entry price/currencyがopening revisionと一致することを確認する。`RiskManagementParameters.Initial`のLong `3.0`、Short `2.5`、partial take-profit `1.5R` / `0.50`を解決してrisk basisへ凍結し、そのbasis IDと算出済み初期ラインをそのまま使うrevision 1・`Initial`・triggerなしのrisk planを同時に返す。算出されたstopまたはtargetが0以下、decimal範囲外、値型のdefaultによる0値、lot/position/opening revision不一致は拒否する。

### Corporate actions while holding
利用者が登録した元約定価格・元約定株数は監査原票として変更しない。分割比率 `r = 新株数 / 旧株数` が保有中に効力発生した場合、企業アクション調整履歴を追加し、現在基準の株数を `r` 倍、取得単価・固定ATR・損切/利確価格を `r` で割る。これらを一組で換算し、単位不一致を作らない。

現金配当は株数・取得単価・固定ATR・価格ラインを変更しない。制度信用の配当金相当額は買建の受取/売建の支払、一般信用は証券会社の契約条件として、企業アクション予測と後日の実額台帳を分離する。権利落ちを警告表示する。端株・現金交付、合併等の未対応イベントは `ReconciliationRequired` とし、照合完了まで当該ポジションの自動再評価を停止する。企業アクション調整から約定履歴を生成してはならない。

## Carrying costs and maturity
ATR損切・利確ラインは価格ベースの説明可能なルールとして維持し、信用取引コストをラインへ混ぜない。買方金利、貸株料、逆日歩、配当金相当額、証券会社固有コストは `MarginCostLedger` で別管理し、次の情報を保持する。

- 対象MarginLot、コスト種別、Charge/Credit、Estimate/Confirmed/Corrected
- 対象期間、数量、金額、通貨、率と単位、日数計算規約
- source、available/observed日時、revision、supersedes

証券会社明細の確定額を正とし、アプリ計算値は見積として明確に区別する。逆日歩の未公表・取得不能、契約料率不明を0円扱いしない。価格損益、確定コスト控除後損益、見積を含むネット参考損益、コスト/R比を分けて表示し、コスト増大を保有見直し理由にできるようにするが、自動決済や約定生成は行わない。

`lot-profit-and-loss-v1`は現在基準のlot数量を`q`、entry/currentの1株価格を`E`/`P`、建玉時の1株リスクを`R`として、Longの価格損益を`(P - E) * q`、Shortを`(E - P) * q`で求める。Chargeを正、Creditを負とするnet costを価格損益から控除し、cost/Rは`net cost / (R * q)`とする。確定コスト控除後損益は全logical itemに解決済みConfirmed leafがある場合だけ算出する。ネット参考損益とcost/Rはitemごとに解決済みConfirmedを優先し、Confirmedが`Unpublished`/`FetchFailed`/`Unknown`ならEstimateへフォールバックする。同じitemのEstimateとConfirmedは同時加算しない。空のコスト集合、選択leaf欠落、未解決状態、通貨・株式単位不一致は0として扱わず、金額と比率をnull相当の型付き欠損にする。`KnownZero`、`NotOccurred`、`NotApplicable`は解決済み0効果として区別したまま保持する。

返済期限はMarginLotごとの証券会社確認値を使い、未決済lotの最短期限と残営業日をポジションへ集約表示する。警告閾値の初期値は30/10/5/1営業日前とし設定化する。期限不明、期限変更、期限接近、期限超過を別状態にし、警告だけで決済済みにしない。

保有画面での表示要件（適用戦略・損切候補・利確候補・HOLD 理由など）は [`product-spec.md`](./product-spec.md) の Positions を参照。
