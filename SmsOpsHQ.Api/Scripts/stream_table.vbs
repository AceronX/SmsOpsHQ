' stream_table.vbs - Stream XPawn Access (.XPD) table rows as JSON lines to stdout.
' Same connection approach as test_sync.py: ODBC Driver + SystemDB (MDW) for workgroup security.
' Usage: cscript //nologo stream_table.vbs <xpdPath> <queryType> <mdwPath> <xpdUser> <xpdPassword>
'   queryType: customers | tickets | items | payments
' Output: One JSON object per line (or {"error":"..."} on failure).

Option Explicit

Dim xpdPath, queryType, mdwPath, xpdUser, xpdPassword
Dim conn, connStr, rs, tableName
Dim field, jsonLine, fieldName, snakeName, val, i
Dim DQ, BS
DQ = Chr(34)
BS = Chr(92)

If WScript.Arguments.Count < 5 Then
  WScript.StdErr.WriteLine "Usage: stream_table.vbs <xpdPath> <queryType> <mdwPath> <xpdUser> <xpdPassword>"
  WScript.Echo "{" & DQ & "error" & DQ & ":" & DQ & "Missing arguments" & DQ & "}"
  WScript.Quit 1
End If

xpdPath = WScript.Arguments(0)
queryType = LCase(Trim(WScript.Arguments(1)))
mdwPath = WScript.Arguments(2)
xpdUser = WScript.Arguments(3)
xpdPassword = WScript.Arguments(4)

' Map queryType to Access table name
Select Case queryType
  Case "customers"
    tableName = "Customers"
  Case "tickets"
    tableName = "Tickets"
  Case "items"
    tableName = "Items"
  Case "payments"
    tableName = "PawnPayments"
  Case Else
    WScript.Echo "{" & DQ & "error" & DQ & ":" & DQ & "Invalid queryType: " & queryType & DQ & "}"
    WScript.Quit 1
End Select

On Error Resume Next
Set conn = CreateObject("ADODB.Connection")
connStr = "Provider=Microsoft.Jet.OLEDB.4.0;" & _
          "Data Source=" & xpdPath & ";" & _
          "Jet OLEDB:System Database=" & mdwPath & ";" & _
          "User Id=" & xpdUser & ";" & _
          "Password=" & xpdPassword & ";"
conn.Open connStr

If Err.Number <> 0 Then
  WScript.Echo "{" & DQ & "error" & DQ & ":" & DQ & JsonEscape(Err.Description) & DQ & "}"
  WScript.Quit 1
End If
On Error Goto 0

On Error Resume Next
Set rs = CreateObject("ADODB.Recordset")
rs.Open "SELECT * FROM [" & tableName & "]", conn, 3, 1

If Err.Number <> 0 Then
  WScript.Echo "{" & DQ & "error" & DQ & ":" & DQ & JsonEscape(Err.Description) & DQ & "}"
  conn.Close
  WScript.Quit 1
End If
On Error Goto 0

Do While Not rs.EOF
  jsonLine = "{"
  For i = 0 To rs.Fields.Count - 1
    Set field = rs.Fields(i)
    fieldName = field.Name
    ' C# XpdSyncService expects "brand" and "metal" for Items (Access has Mfg, Karat)
    If tableName = "Items" And fieldName = "Mfg" Then
      snakeName = "brand"
    ElseIf tableName = "Items" And fieldName = "Karat" Then
      snakeName = "metal"
    Else
      snakeName = PascalToSnake(fieldName)
    End If
    If i > 0 Then
      jsonLine = jsonLine & ","
    End If
    val = FieldToJsonValue(field)
    jsonLine = jsonLine & DQ & snakeName & DQ & ":" & val
  Next
  jsonLine = jsonLine & "}"
  WScript.Echo jsonLine
  rs.MoveNext
Loop

rs.Close
conn.Close
WScript.Quit 0

' Convert PascalCase to snake_case (e.g. CustomerKey -> customer_key)
Function PascalToSnake(str)
  Dim j, c, result, prevUpper, n
  result = ""
  prevUpper = False
  n = Len(str)
  For j = 1 To n
    c = Mid(str, j, 1)
    If c >= "A" And c <= "Z" Then
      If Len(result) > 0 And Not prevUpper Then
        result = result & "_"
      End If
      result = result & LCase(c)
      prevUpper = True
    Else
      result = result & LCase(c)
      prevUpper = False
    End If
  Next
  PascalToSnake = result
End Function

' Return JSON value (number, null, or "quoted string") for a field
Function FieldToJsonValue(fld)
  Dim v, vType
  If IsNull(fld.Value) Or IsEmpty(fld.Value) Then
    FieldToJsonValue = "null"
    Exit Function
  End If
  v = fld.Value
  vType = VarType(v)
  ' vbInteger=2, vbLong=3, vbSingle=4, vbDouble=5, vbCurrency=6, vbDecimal=14
  If vType = 2 Or vType = 3 Or vType = 4 Or vType = 5 Or vType = 6 Or vType = 14 Then
    FieldToJsonValue = Replace(Replace(CStr(v), ",", "."), " ", "")
    Exit Function
  End If
  If vType = 11 Then
    If v Then
      FieldToJsonValue = "-1"
    Else
      FieldToJsonValue = "0"
    End If
    Exit Function
  End If
  If vType = 7 Then
    FieldToJsonValue = Chr(34) & JsonEscape(FormatISODate(v)) & Chr(34)
    Exit Function
  End If
  FieldToJsonValue = Chr(34) & JsonEscape(CStr(v)) & Chr(34)
End Function

Function FormatISODate(d)
  Dim y, m, dy, h, mn, sc
  y = Year(d)
  m = Month(d)
  dy = Day(d)
  h = Hour(d)
  mn = Minute(d)
  sc = Second(d)
  FormatISODate = Right("0000" & y, 4) & "-" & Right("00" & m, 2) & "-" & Right("00" & dy, 2) & "T" & _
    Right("00" & h, 2) & ":" & Right("00" & mn, 2) & ":" & Right("00" & sc, 2)
End Function

Function JsonEscape(s)
  Dim out, j, c
  If IsNull(s) Then
    JsonEscape = ""
    Exit Function
  End If
  If Len(s) = 0 Then
    JsonEscape = ""
    Exit Function
  End If
  out = ""
  For j = 1 To Len(s)
    c = Mid(s, j, 1)
    If c = Chr(92) Then
      out = out & Chr(92) & Chr(92)
    ElseIf c = Chr(34) Then
      out = out & Chr(92) & Chr(34)
    ElseIf c = Chr(8) Then
      out = out & Chr(92) & "b"
    ElseIf c = Chr(9) Then
      out = out & Chr(92) & "t"
    ElseIf c = Chr(10) Then
      out = out & Chr(92) & "n"
    ElseIf c = Chr(12) Then
      out = out & Chr(92) & "f"
    ElseIf c = Chr(13) Then
      out = out & Chr(92) & "r"
    Else
      out = out & c
    End If
  Next
  JsonEscape = out
End Function
