// Every check works in a folder of its own, so they run in parallel.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
