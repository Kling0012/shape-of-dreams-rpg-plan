using System;

namespace SodRpg.Core
{
    /// <summary>台帳ファイルの構造が不正（壊れたJSON、必須欄の欠落、値の形式が違うなど）。</summary>
    public class LedgerFormatException : Exception
    {
        public LedgerFormatException(string message) : base(message) { }
    }

    /// <summary>保存形式の版が新しすぎる、または対応しない。読み込みを止める。</summary>
    public sealed class LedgerVersionException : LedgerFormatException
    {
        public LedgerVersionException(string message) : base(message) { }
    }

    /// <summary>
    /// ファイルは存在するが、本体・一時・バックアップのどれも有効でない。
    /// 黙って空の台帳を作らず、利用者へ復旧手順を示すために投げる。
    /// </summary>
    public sealed class LedgerCorruptException : Exception
    {
        public LedgerCorruptException(string message) : base(message) { }
    }
}
