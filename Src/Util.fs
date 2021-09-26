namespace AvalonLog

/// Shadows the ignore function to only accept structs
/// This is to prevent accidetially ignoring partially aplied functions that would return struct
module internal Util = 

    /// Shadows the original 'ignore' function
    /// This is to prevent accidetially ignoring partially applied functions
    /// This 'ignore' only work on value types (struct),
    /// Reference types like objects and functions need to be ignored with 'ignoreObj'
    let inline ignore (x:'T when 'T: struct) = ()

    /// Ignores any object (but not struct)
    /// For structs use 'ignore'
    let inline ignoreObj (x:obj) = ()

    /// The same as 'not isNull'
    let inline notNull x = match x with null -> false | _ -> true
