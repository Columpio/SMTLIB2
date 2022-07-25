module SMTLIB2.Context
open System.Collections.Generic
open System.Collections.Immutable

type Context() as this =
    let operations = Dictionary<_, _>()
    let adts = Dictionary<ident, (operation * operation * operation list) list>() // adt-sort-name |-> [tester, constructor, [selector]]
    let freeSorts = HashSet<ident>()
    let predefinedSymbols = ImmutableHashSet.Create<ident>("Bool", "Int", "Array", "=", "distinct", "<", ">", "<=", ">=", "+", "-", "*", "mod", "div")
    do this.Clear()

    member x.Clear () =
        operations.Clear()
        Dictionary.copyContents operations Operations.elementaryOperations
        adts.Clear()
        freeSorts.Clear()
    
    member x.IsPredefinedSymbol s = predefinedSymbols.Contains(s)

    member x.FindSort ident argSorts =
        match ident, argSorts with
        | "Bool", [] ->
            assert (List.isEmpty argSorts)
            BoolSort
        | "Int", [] ->
            assert (List.isEmpty argSorts)
            IntSort
        | _, [] when freeSorts.Contains(ident) -> FreeSort ident
        | _, [] when adts.ContainsKey(ident) -> ADTSort ident
        | "Array", [s1; s2] -> ArraySort(s1, s2)
        | _ -> failwith $"sort {ident} with arguments {argSorts} is not known"

    member x.TryFindDefinedOperation ident = Dictionary.tryFind ident operations

    member x.FillOperation opName argTypes =
        opt {
            let! op = x.TryFindDefinedOperation opName
            if Operation.argumentTypes op = argTypes then return op
        } |> Option.defaultWith (fun () -> Operations.findAndInstantiateGenericOperation opName argTypes)

    member x.AddOperation name op = operations.Add(name, op)

    member x.AddFreeSort name = freeSorts.Add(name) |> ignore

    member x.AddRawADTSort adtname =
        adts.Add(adtname, [])

    member x.AddRawADTOperations adtname constrs = // [constr, [selector, argSort]]
        let adtSort = x.FindSort adtname []
        let handleConstr (constr, selAndArgs) =
            let constrOp = Operation.makeElementaryOperationFromVars constr selAndArgs adtSort
            let sels = List.map (fun (sel, s) -> sel, Operation.makeElementaryOperationFromSorts sel [adtSort] s) selAndArgs
            let testerName = ADTExtensions.getTesterNameFromConstructor constr
            let testerOp = Operation.makeElementaryOperationFromSorts testerName [adtSort] BoolSort
            x.AddOperation constr constrOp
            x.AddOperation testerName testerOp
            for name, op in sels do
                x.AddOperation name op
            constrOp, testerOp, List.map snd sels
        let constrs = List.map handleConstr constrs
        adts.[adtname] <- constrs
        constrs

    member x.TryGetTesters adtSort =
        opt {
            let! adtName = ADTExtensions.tryGetADTName adtSort
            let! adtOps = Dictionary.tryFind adtName adts
            return List.map snd3 adtOps
        }

    member x.TryGetConstructors adtSort =
        opt {
            let! adtName = ADTExtensions.tryGetADTName adtSort
            let! adtOps = Dictionary.tryFind adtName adts
            return List.map fst3 adtOps
        }

    member x.GetConstructors adtSort =
        match x.TryGetConstructors adtSort with
        | Some cs -> cs
        | None -> failwith $"no such adt sort {adtSort}"

    member x.IsConstructor op =
        match x.TryGetConstructors (Operation.returnType op) with
        | Some symbs -> List.contains op symbs
        | None -> false

//    member x.tryFind opName = Map.tryFind opName operations
//    member x.find opName = Map.find opName operations
//    member x.m_addOperation name op = operations <- Map.add name op operations
//    member x.addOperation name op = Context(Map.add name op operations, adts)
//    member x.m_addADTConstructors name nos =
//        let names = Map.toList nos |> List.map (fun (c, _) -> makeConstructorTesterPair c (testerNameOf c))
//        operations <- Map.union operations nos
//        adts <- Map.add name names adts
//    member x.addADTConstructors name nos =
//        let names = Map.toList nos |> List.map (fun (c, _) -> makeConstructorTesterPair c (testerNameOf c))
//        Context(Map.union operations nos, Map.add name names adts)
//    member x.containsKey key = Map.containsKey key operations
//    member x.tryGetConstructors adtName = Map.tryFind adtName adts |> Option.map (List.map takeConstructor)
//    member x.getConstructors adtName = x.tryGetConstructors adtName |> Option.get
//    member x.tryGetTesters adtName = Map.tryFind adtName adts |> Option.map (List.map takeTester)
//
//    member x.m_addADTOperations adtname fname selectors =
//        let constr_op = Operation.makeElementaryOperationFromVars fname selectors adtname
//        List.iter (fun (pr, s) -> x.m_addOperation pr (Operation.makeElementaryOperationFromSorts pr [adtname] s)) selectors
//        x.m_addOperation fname constr_op
//        let testerName = testerNameOf fname
//        x.m_addOperation testerName (testerOpOf testerName adtname)
//        constr_op
//
    member x.AddToCTXCommand c =
        let extendDecl ((name, _) as dt) =
            x.AddRawADTSort name
            x.AddRawADTOperations name (snd <| ADTExtensions.adtDefinitionToRaw dt)
            |> ignore
        match c with
        | DeclareDatatype dt -> extendDecl dt
        | DeclareDatatypes dts -> List.iter extendDecl dts
        | DeclareConst(name, sort) -> x.AddOperation name (Operation.makeUserOperationFromSorts name [] sort)
        | DeclareFun(name, argTypes, sort) ->
            x.AddOperation name (Operation.makeUserOperationFromSorts name argTypes sort)
        | _ -> ()

    member x.AddToCTXOriginalCommand c =
        let extendDef (name, vars, sort, _) = x.AddOperation name (Operation.makeUserOperationFromVars name vars sort)
        match c with
        | Definition(DefineFunRec df)
        | Definition(DefineFun df) -> extendDef df
        | Definition(DefineFunsRec dfs) -> dfs |> List.iter extendDef
        | Command c -> x.AddToCTXCommand c
        | _ -> ()
