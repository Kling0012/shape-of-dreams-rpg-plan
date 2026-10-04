using Xunit;

// 言語の切り替え（Loc.Japanese）は全体で1つなので、試験を並行して走らせると互いに干渉する。順番に走らせる。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
