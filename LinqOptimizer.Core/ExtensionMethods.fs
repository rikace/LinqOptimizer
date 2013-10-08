﻿    
namespace LinqOptimizer.Core
    
    open System
    open System.Collections
    open System.Collections.Generic
    open System.Linq
    open System.Linq.Expressions
    open System.Reflection

    // LINQ-C# friendly extension methods 
    [<AutoOpen>]
    [<System.Runtime.CompilerServices.Extension>]
    type ExtensionMethods =

        // SequentialQuery ExtensionMethods

        [<System.Runtime.CompilerServices.Extension>]
        static member AsQueryExpr(enumerable : IEnumerable<'T>) = 
            // Hack to optimize Enumerable.Range and Enumerable.Repeat calls
            // TODO : check Mono generated types
            let ty = enumerable.GetType()
            match ty.FullName with
            | s when s.StartsWith "System.Linq.Enumerable+<RangeIterator>"  ->
                let start = ty.GetFields().First(fun f -> f.Name.EndsWith "__start").GetValue(enumerable)
                let count = ty.GetFields().First(fun f -> f.Name.EndsWith "__count").GetValue(enumerable)
                new QueryExpr<IEnumerable<'T>>(RangeGenerator(start :?> int, count :?> int))
            | s when s.StartsWith "System.Linq.Enumerable+<RepeatIterator>"  ->
                let element = ty.GetFields().First(fun f -> f.Name.EndsWith "__element").GetValue(enumerable)
                let count = ty.GetFields().First(fun f -> f.Name.EndsWith "__count").GetValue(enumerable)
                new QueryExpr<IEnumerable<'T>>(RepeatGenerator(element, typeof<'T>, count :?> int))
            | _ -> new QueryExpr<IEnumerable<'T>>(Source (constant enumerable, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Compile<'T>(queryExpr : QueryExpr<'T>) : Func<'T> =
            let expr = Compiler.compileToSequential queryExpr.QueryExpr
            let func = Expression.Lambda<Func<'T>>(expr).Compile()
            func

        [<System.Runtime.CompilerServices.Extension>]
        static member Compile(queryExpr : QueryExprVoid) : Action =
            let expr = Compiler.compileToSequential queryExpr.QueryExpr
            let action = Expression.Lambda<Action>(expr).Compile()
            action
            
        [<System.Runtime.CompilerServices.Extension>]
        static member Run<'T>(queryExpr : QueryExpr<'T>) : 'T =
            ExtensionMethods.Compile(queryExpr).Invoke()

        [<System.Runtime.CompilerServices.Extension>]
        static member Run(queryExpr : QueryExprVoid) : unit =
            ExtensionMethods.Compile(queryExpr).Invoke()

        [<System.Runtime.CompilerServices.Extension>]
        static member Select<'T, 'R>(queryExpr : QueryExpr<IEnumerable<'T>>, selector : Expression<Func<'T, 'R>>) =
            new QueryExpr<IEnumerable<'R>>(Transform (selector, queryExpr.QueryExpr, typeof<'R>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Select<'T, 'R>(queryExpr : QueryExpr<IEnumerable<'T>>, selector : Expression<Func<'T, int, 'R>>) =
            new QueryExpr<IEnumerable<'R>>(TransformIndexed (selector, queryExpr.QueryExpr, typeof<'R>))
            
        [<System.Runtime.CompilerServices.Extension>]
        static member Where<'T>(queryExpr : QueryExpr<IEnumerable<'T>>, predicate : Expression<Func<'T, bool>>) =
            new QueryExpr<IEnumerable<'T>>(Filter (predicate, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Where<'T>(queryExpr : QueryExpr<IEnumerable<'T>>, predicate : Expression<Func<'T, int, bool>>) =
            new QueryExpr<IEnumerable<'T>>(FilterIndexed (predicate, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Aggregate(queryExpr : QueryExpr<IEnumerable<'T>>, seed : 'Acc, func : Expression<Func<'Acc, 'T, 'Acc>>) =
            new QueryExpr<'Acc>(Aggregate ((seed :> _, typeof<'Acc>), func, queryExpr.QueryExpr))

        [<System.Runtime.CompilerServices.Extension>]
        static member Sum(queryExpr : QueryExpr<IEnumerable<double>>) =
            new QueryExpr<double>(Sum (queryExpr.QueryExpr, typeof<double>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Sum(queryExpr : QueryExpr<IEnumerable<int>>) =
            new QueryExpr<int>(Sum (queryExpr.QueryExpr, typeof<int>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Count(queryExpr : QueryExpr<IEnumerable<'T>>) =
            new QueryExpr<'T>(Count (queryExpr.QueryExpr, typeof<'T>))
            
        [<System.Runtime.CompilerServices.Extension>]
        static member SelectMany<'T, 'Col, 'R>(queryExpr : QueryExpr<IEnumerable<'T>>, 
                                                collectionSelector : Expression<Func<'T, IEnumerable<'Col>>>, 
                                                resultSelector : Expression<Func<'T, 'Col, 'R>>) : QueryExpr<IEnumerable<'R>> =
            let queryExpr' = 
                match collectionSelector with
                | Lambda ([paramExpr], bodyExpr) ->
                    NestedQueryTransform ((paramExpr, Compiler.toQueryExpr bodyExpr), resultSelector, queryExpr.QueryExpr, typeof<'R>)
                | _ -> failwithf "Invalid state %A" collectionSelector

            new QueryExpr<IEnumerable<'R>>(queryExpr')

        [<System.Runtime.CompilerServices.Extension>]
        static member SelectMany<'T, 'R>(queryExpr : QueryExpr<IEnumerable<'T>>, selector : Expression<Func<'T, IEnumerable<'R>>>) : QueryExpr<IEnumerable<'R>> =
            let queryExpr' = 
                match selector with
                | Lambda ([paramExpr], bodyExpr) ->
                    NestedQuery ((paramExpr, Compiler.toQueryExpr bodyExpr), queryExpr.QueryExpr, typeof<'R>)
                | _ -> failwithf "Invalid state %A" selector

            new QueryExpr<IEnumerable<'R>>(queryExpr')


        [<System.Runtime.CompilerServices.Extension>]
        static member Take<'T>(queryExpr : QueryExpr<IEnumerable<'T>>, n : int) : QueryExpr<IEnumerable<'T>> =
            new QueryExpr<IEnumerable<'T>>(Take (constant n, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Skip<'T>(queryExpr : QueryExpr<IEnumerable<'T>>, n : int) : QueryExpr<IEnumerable<'T>> =
            new QueryExpr<IEnumerable<'T>>(Skip (constant n, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member ForEach<'T>(queryExpr : QueryExpr<IEnumerable<'T>>, action : Expression<Action<'T>>) : QueryExprVoid =
            new QueryExprVoid(ForEach (action, queryExpr.QueryExpr))

        [<System.Runtime.CompilerServices.Extension>]
        static member GroupBy<'T, 'Key>(queryExpr : QueryExpr<IEnumerable<'T>>, keySelector : Expression<Func<'T, 'Key>>)
                                : QueryExpr<IEnumerable<IGrouping<'Key, 'T>>> = 
            new QueryExpr<IEnumerable<IGrouping<'Key, 'T>>>(GroupBy (keySelector, queryExpr.QueryExpr, typeof<IGrouping<'Key, 'T>>))

        [<System.Runtime.CompilerServices.Extension>]
        static member OrderBy<'T, 'Key>(queryExpr : QueryExpr<IEnumerable<'T>>, keySelector : Expression<Func<'T, 'Key>>)
                                : QueryExpr<IEnumerable<'T>> = 
            new QueryExpr<IEnumerable<'T>>(OrderBy (keySelector, Order.Ascending, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member OrderByDescending<'T, 'Key>(queryExpr : QueryExpr<IEnumerable<'T>>, keySelector : Expression<Func<'T, 'Key>>)
                                : QueryExpr<IEnumerable<'T>> = 
            new QueryExpr<IEnumerable<'T>>(OrderBy (keySelector, Order.Descending, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member ToList(queryExpr : QueryExpr<IEnumerable<'T>>) : QueryExpr<List<'T>> = 
            new QueryExpr<List<'T>>(ToList queryExpr.QueryExpr)

        [<System.Runtime.CompilerServices.Extension>]
        static member ToArray(queryExpr : QueryExpr<IEnumerable<'T>>) : QueryExpr<'T []> = 
            new QueryExpr<'T []>(ToArray queryExpr.QueryExpr)

        // ParallelQuery ExtensionMethods
        [<System.Runtime.CompilerServices.Extension>]
        static member AsQueryExpr(parallelQuery : ParallelQuery<'T>) = 
            new ParallelQueryExpr<IEnumerable<'T>>(Source (constant parallelQuery, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Compile<'T>(queryExpr : ParallelQueryExpr<'T>) : Func<'T> =
            let expr = Compiler.compileToParallel queryExpr.QueryExpr
            let func = Expression.Lambda<Func<'T>>(expr).Compile()
            func

        [<System.Runtime.CompilerServices.Extension>]
        static member Run<'T>(queryExpr : ParallelQueryExpr<'T>) : 'T =
            ExtensionMethods.Compile(queryExpr).Invoke()

        [<System.Runtime.CompilerServices.Extension>]
        static member Select<'T, 'R>(queryExpr : ParallelQueryExpr<IEnumerable<'T>>, selector : Expression<Func<'T, 'R>>) =
            new ParallelQueryExpr<IEnumerable<'R>>(Transform (selector, queryExpr.QueryExpr, typeof<'R>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Where<'T>(queryExpr : ParallelQueryExpr<IEnumerable<'T>>, predicate : Expression<Func<'T, bool>>) =
            new ParallelQueryExpr<IEnumerable<'T>>(Filter (predicate, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Sum(queryExpr : ParallelQueryExpr<IEnumerable<double>>) =
            new ParallelQueryExpr<double>(Sum (queryExpr.QueryExpr, typeof<double>))

        [<System.Runtime.CompilerServices.Extension>]
        static member Sum(queryExpr : ParallelQueryExpr<IEnumerable<int>>) =
            new ParallelQueryExpr<int>(Sum (queryExpr.QueryExpr, typeof<int>))

        [<System.Runtime.CompilerServices.Extension>]
        static member SelectMany<'T, 'R>(queryExpr : ParallelQueryExpr<IEnumerable<'T>>, selector : Expression<Func<'T, IEnumerable<'R>>>) =
            let queryExpr' = 
                match selector with
                | Lambda ([paramExpr], bodyExpr) ->
                    NestedQuery ((paramExpr, Compiler.toQueryExpr bodyExpr), queryExpr.QueryExpr, typeof<'R>)
                | _ -> failwithf "Invalid state %A" selector

            new ParallelQueryExpr<IEnumerable<'R>>(queryExpr')

        [<System.Runtime.CompilerServices.Extension>]
        static member SelectMany<'T, 'Col, 'R>(queryExpr : ParallelQueryExpr<IEnumerable<'T>>, 
                                                collectionSelector : Expression<Func<'T, IEnumerable<'Col>>>, 
                                                resultSelector : Expression<Func<'T, 'Col, 'R>>) =
            let queryExpr' = 
                match collectionSelector with
                | Lambda ([paramExpr], bodyExpr) ->
                    NestedQueryTransform ((paramExpr, Compiler.toQueryExpr bodyExpr), resultSelector, queryExpr.QueryExpr, typeof<'R>)
                | _ -> failwithf "Invalid state %A" collectionSelector

            new ParallelQueryExpr<IEnumerable<'R>>(queryExpr')

        [<System.Runtime.CompilerServices.Extension>]
        static member GroupBy<'T, 'Key>(queryExpr : ParallelQueryExpr<IEnumerable<'T>>, keySelector : Expression<Func<'T, 'Key>>) =
            new ParallelQueryExpr<IEnumerable<IGrouping<'Key, 'T>>>(GroupBy (keySelector, queryExpr.QueryExpr, typeof<IGrouping<'Key, 'T>>))

        [<System.Runtime.CompilerServices.Extension>]
        static member OrderBy<'T, 'Key>(queryExpr : ParallelQueryExpr<IEnumerable<'T>>, keySelector : Expression<Func<'T, 'Key>>) = 
            new ParallelQueryExpr<IEnumerable<'T>>(OrderBy (keySelector, Order.Ascending, queryExpr.QueryExpr, typeof<'T>))

        [<System.Runtime.CompilerServices.Extension>]
        static member OrderByDescending<'T, 'Key>(queryExpr : ParallelQueryExpr<IEnumerable<'T>>, keySelector : Expression<Func<'T, 'Key>>) = 
            new ParallelQueryExpr<IEnumerable<'T>>(OrderBy (keySelector, Order.Descending, queryExpr.QueryExpr, typeof<'T>))
