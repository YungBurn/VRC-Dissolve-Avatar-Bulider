# YB Dissolve Avatar Builder

[**简体中文**](README.md) | [**English**](README.en.md) | [**日本語**](README.ja.md)

**バージョン：** 0.2.0 · **作者：** YungBurn · **ライセンス：** [MIT License](LICENSE)

**Unity / VRChat アバター向け**の、衣装切り替え用スキャン・ディゾルブ効果を作成するエディターツールです。2種類の衣装を解析し、必要なベイク済みメッシュとマテリアルを生成します。必要に応じて Animator、VRChat Action Controller、Modular Avatar の設定も行えます。

> 本リポジトリには編集可能なソースコード、シェーダー、ドキュメント、Unity `.meta` ファイルが含まれます。Unity プロジェクト一式、アバターモデル、ユーザーが生成したアセットは含まれません。

## 主な機能

- **2着の衣装切り替え：** Outfit A / B の複数の Root と Renderer を解析し、導入結果を検証します。
- **方向指定スキャン：** Avatar Root のローカル座標でスキャン方向を指定し、座標を `UV4.x` にベイクします。方向変更後は再ベイクが必要です。
- **双方向の独立設定：** A→B と B→A のスキャン開始前の遅延を別々に設定し、アクションの長さに合わせられます。
- **マテリアル表現：** スキャン境界、発光、Overlay、Face / Body のマテリアル切り替え、複数の lilToon 描画モードに対応します。
- **コントローラー連携：** FX Controller を生成し、必要に応じて VRChat Action Controller、Modular Avatar のメニュー／パラメーターを設定します。
- **元に戻せる運用：** インストール、検証、削除、復元に関する機能を備えています。
- **3言語 UI：** 中国語（初回の既定値）、英語、日本語。選択した言語は Unity の `EditorPrefs` にローカル保存されます。

## 必要な環境

| コンポーネント | 用途 | 必要性 |
| --- | --- | --- |
| Unity Editor | ツールの実行 | 必須。VRChat プロジェクトに対応するバージョンを使用してください |
| [lilToon](https://github.com/lilxyzw/lilToon)（`jp.lilxyzw.liltoon`） | Shader Include / ShaderGUI、マテリアル変換 | 必須 |
| VRChat SDK Avatars | VRChat Action 機能 | 該当機能を使う場合に必要 |
| [Modular Avatar](https://modular-avatar.nadena.dev/) | FX、Parameters、Menu の自動設定 | MA 連携を使う場合に必要 |

**注意：** 付属 Shader は `Packages/jp.lilxyzw.liltoon/Shader/Includes/` を参照します。指定されたパッケージパスに lilToon をインストールしてください。**lilToon 本体は同梱していません。**

## インストール

1. Unity プロジェクトに lilToon など必要な依存関係を導入します。
2. このリポジトリをダウンロードまたはクローンし、**`_YBTools/`** ディレクトリ全体を Unity プロジェクトの **`Assets/_YBTools/`** にコピーします。
3. ディレクトリ名と `.meta` ファイルを維持してください。Unity の `Packages/` や別のパスには配置**しないでください**。
4. Unity のインポートとコンパイルを待ち、**`YBTools > 溶解 > Dissolve Avatar Builder`** からツールを起動します（メニュー名は UI 言語を変更しても固定です）。

## クイックスタート

1. **Avatar** で Avatar Root、Outfit A / B の Root、Profile を設定します。
2. **解析** で Renderer、マテリアル、Read/Write、UV4 の状態を確認します。
3. **エフェクト** でスキャン方向、範囲、A→B / B→A の遅延を設定します。
4. **生成 / 設置** でベイク済みメッシュとマテリアルを生成し、検証結果を確認して導入します。
5. 必要に応じて **材質切替** で Face / Body を設定し、**Animator / MA** で FX、Action、MA の構成を生成します。
6. **検証** を実行し、必要に応じて Unity Play Mode や VRChat 環境で動作確認します。

**重要：** スキャン方向を変更しても既存メッシュの `UV4.x` は自動更新されません。対象メッシュを再ベイクし、関連アセットを再導入してください。

## ディレクトリ構成

```text
.
├── README.md / README.en.md / README.ja.md
├── LICENSE                        # YungBurn のオリジナル部分：MIT
├── THIRD_PARTY_NOTICES.md         # サードパーティーの著作権・ライセンス
└── _YBTools/                      # Unity プロジェクトの Assets/ にコピー
    ├── Editor/                    # UI、生成器、インストーラー
    ├── Runtime/                   # 設定データ、導入記録
    ├── Shader/                    # スキャン Shader、互換バージョン
    ├── Data/Schema/               # バージョン・Shader メタデータ
    └── Documentation/             # 導入・使用・ライセンスの説明
```

詳しくは [`QuickStart.md`](_YBTools/Documentation/QuickStart.md) · [`Installation.md`](_YBTools/Documentation/Installation.md) · [`MaterialSupport.md`](_YBTools/Documentation/MaterialSupport.md) · [`Troubleshooting.md`](_YBTools/Documentation/Troubleshooting.md) を参照してください。

## ライセンスと著作権表示

**YungBurn によるオリジナルのコードおよびドキュメント**は [MIT License](LICENSE) で公開されます。**Copyright © 2026 YungBurn.**

これらの部分は利用、複製、改変、再配布、サブライセンス、商用利用が可能です。**ソフトウェアの全部または実質的な部分を再配布する場合、元の著作権表示と MIT ライセンス全文を保持する必要があります。** これにより YungBurn の著作権表示が保持されます。派生作品の README などに「Based on YB Dissolve Avatar Builder by YungBurn」と記載していただけると幸いですが、MIT ライセンス自体は UI や広告での追加クレジット表示までは義務付けていません。

付属 Shader の一部には lilToon 由来のコード／構造が含まれます。lilToon 側の MIT ライセンスと著作権表示も保持してください。詳細は [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) を参照してください。

## 互換性とサポート

使用する Unity、VRChat SDK、lilToon、Modular Avatar のバージョンに合わせて実際のコンパイル・動作確認を行ってください。パッケージの静的検査は **Unity / VRChat 上での全機能テストを意味しません**。

不具合報告には、再現手順、Unity と依存パッケージのバージョン、関係する Console ログ（個人情報を削除したもの）を GitHub Issues に添えてください。
