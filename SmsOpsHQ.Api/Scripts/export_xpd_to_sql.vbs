' export_xpd_to_sql.vbs - Export XPawn Access tables to a SQL file for fast bulk load.
' Like final_sync: one connection, write INSERT statements to file, then SQLite can .read it.
' Usage: cscript //nologo export_xpd_to_sql.vbs <xpdPath> <mdwPath> <user> <password> <outputPath>
' Output: SQL file with BEGIN TRANSACTION; INSERT...; COMMIT; (one statement per line).

Option Explicit

Const adOpenStatic = 3
Const adLockReadOnly = 1

Dim xpdPath, mdwPath, xpdUser, xpdPassword, outputPath
Dim conn, connStr, rs, fso, outFile, nowIso

If WScript.Arguments.Count < 5 Then
  WScript.StdErr.WriteLine "Usage: export_xpd_to_sql.vbs <xpdPath> <mdwPath> <user> <password> <outputPath>"
  WScript.Quit 1
End If

xpdPath = WScript.Arguments(0)
mdwPath = WScript.Arguments(1)
xpdUser = WScript.Arguments(2)
xpdPassword = WScript.Arguments(3)
outputPath = WScript.Arguments(4)

nowIso = FormatISODate(Now())

Set fso = CreateObject("Scripting.FileSystemObject")
Set outFile = fso.CreateTextFile(outputPath, True, True)
outFile.WriteLine "BEGIN TRANSACTION;"

' Connect: ACE first, then Jet fallback
On Error Resume Next
Set conn = CreateObject("ADODB.Connection")
connStr = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" & xpdPath & ";Jet OLEDB:System Database=" & mdwPath & ";User Id=" & xpdUser & ";Password=" & xpdPassword & ";"
conn.Open connStr
If Err.Number <> 0 Then
  Err.Clear
  connStr = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" & xpdPath & ";Jet OLEDB:System Database=" & mdwPath & ";User Id=" & xpdUser & ";Password=" & xpdPassword & ";"
  conn.Open connStr
End If
If Err.Number <> 0 Then
  outFile.Close
  WScript.StdErr.WriteLine "Connect failed: " & Err.Description
  WScript.Quit 1
End If
On Error Goto 0

' Export each table
WriteCustomersSQL conn, outFile, nowIso
WriteTableSQL conn, outFile, "Tickets", "INSERT OR REPLACE INTO Tickets (Key,CustomerKey,TransNo,Type,Active,Amount,CurrentBalance,IssueDate,DueDate,DateClosed,HowClosed,Status,Notes,Item,OperatorInitials,GunTicket,LostTicket,PaidTillDate,LastDate,ChargesDue,StandardCharges,StandardPU,FullTermPU,FulltermRenew,SyncedAt) VALUES ", Array("Key","CustomerKey","TransNo","Type","Active","Amount","CurrentBalance","IssueDate","DueDate","DateClosed","HowClosed","Status","Notes","Item","OperatorInitials","GunTicket","LostTicket","PaidTillDate","LastDate","ChargesDue","StandardCharges","StandardPU","FullTermPU","FulltermRenew"), nowIso, 25
WriteTableSQL conn, outFile, "Items", "INSERT OR REPLACE INTO Items (Key,TicketKey,PrintedDetail,CategoryCode,SerialNo,Cost,ItemStatus,Notes,Mfg,Model,Color,Size,Weight,Karat,SyncedAt) VALUES ", Array("Key","TicketKey","PrintedDetail","CategoryCode","SerialNo","Cost","ItemStatus","Notes","Mfg","Model","Color","Size","Weight","Karat"), nowIso, 15
' Payments: XPD may use "PawnPayment" or "PawnPayments"; try both. We write to SQLite "PawnPayments".
WritePawnPaymentsSQL conn, outFile, nowIso

conn.Close
outFile.WriteLine "COMMIT;"
outFile.Close
WScript.Quit 0

Sub WriteCustomersSQL(conn, outFile, nowIso)
  Dim rs, sql, i, colNames, vals
  Set rs = CreateObject("ADODB.Recordset")
  On Error Resume Next
  rs.Open "SELECT Key,LastName,FirstName,MiddleName,Address,City,State,Zip,ResPhone,BusPhone,EMailAddress,DOB,SSN,IDNo,IDIssueState,Notes,FirstTransaction,LastTransaction,Warning FROM Customers", conn, adOpenStatic, adLockReadOnly
  If Err.Number <> 0 Then
    WScript.StdErr.WriteLine "Customers: " & Err.Description
    Exit Sub
  End If
  On Error Goto 0
  Do Until rs.EOF
    sql = "INSERT INTO Customers (CustomerKey,LastName,FirstName,MiddleName,Address,City,State,Zip,ResPhone,BusPhone,EMailAddress,DOB,SSN,IDNo,IDIssueState,Notes,FirstTransaction,LastTransaction,Warning,SyncedAt,PhoneE164,StoreId,CreatedAt,UpdatedAt) VALUES ("
    sql = sql & SqlVal(rs("Key")) & ","
    sql = sql & SqlStr(rs("LastName")) & "," & SqlStr(rs("FirstName")) & "," & SqlStr(rs("MiddleName")) & ","
    sql = sql & SqlStr(rs("Address")) & "," & SqlStr(rs("City")) & "," & SqlStr(rs("State")) & "," & SqlStr(rs("Zip")) & ","
    sql = sql & SqlStr(rs("ResPhone")) & "," & SqlStr(rs("BusPhone")) & "," & SqlStr(rs("EMailAddress")) & ","
    sql = sql & SqlDate(rs("DOB")) & "," & SqlStr(rs("SSN")) & "," & SqlStr(rs("IDNo")) & "," & SqlStr(rs("IDIssueState")) & ","
    sql = sql & SqlStr(rs("Notes")) & "," & SqlDate(rs("FirstTransaction")) & "," & SqlDate(rs("LastTransaction")) & "," & SqlStr(rs("Warning")) & ","
    sql = sql & "'" & Replace(nowIso, "'", "''") & "','',1,'" & Replace(nowIso, "'", "''") & "','" & Replace(nowIso, "'", "''") & "')"
    sql = sql & " ON CONFLICT(CustomerKey) DO UPDATE SET LastName=excluded.LastName,FirstName=excluded.FirstName,MiddleName=excluded.MiddleName,Address=excluded.Address,City=excluded.City,State=excluded.State,Zip=excluded.Zip,ResPhone=excluded.ResPhone,BusPhone=excluded.BusPhone,EMailAddress=excluded.EMailAddress,DOB=excluded.DOB,SSN=excluded.SSN,IDNo=excluded.IDNo,IDIssueState=excluded.IDIssueState,Notes=excluded.Notes,FirstTransaction=excluded.FirstTransaction,LastTransaction=excluded.LastTransaction,Warning=excluded.Warning,SyncedAt=excluded.SyncedAt,UpdatedAt=excluded.UpdatedAt;"
    outFile.WriteLine sql
    rs.MoveNext
  Loop
  rs.Close
End Sub

Sub WriteTableSQL(conn, outFile, tableName, prefix, colNames, nowIso, syncedAtIndex)
  Dim rs, sql, i, v
  Set rs = CreateObject("ADODB.Recordset")
  On Error Resume Next
  rs.Open "SELECT * FROM [" & tableName & "]", conn, adOpenStatic, adLockReadOnly
  If Err.Number <> 0 Then
    WScript.StdErr.WriteLine tableName & ": " & Err.Description
    Exit Sub
  End If
  On Error Goto 0
  Do Until rs.EOF
    sql = prefix & "("
    For i = 0 To UBound(colNames)
      If i > 0 Then sql = sql & ","
      Dim fldName
      fldName = colNames(i)
      If fldName = "Check" Then fldName = "[Check]"
      v = rs.Fields(fldName).Value
      sql = sql & FormatSqlVal(v, rs.Fields(fldName).Type)
    Next
    sql = sql & ",'" & Replace(nowIso, "'", "''") & "');"
    outFile.WriteLine sql
    rs.MoveNext
  Loop
  rs.Close
End Sub

' Export PawnPayments directly with actual XPD column names (verified by inspecting database).
' Reads 21 columns from XPD, writes to SQLite PawnPayments with Check mapped to Check_.
Sub WritePawnPaymentsSQL(conn, outFile, nowIso)
  Dim rs, sql, cnt
  Set rs = CreateObject("ADODB.Recordset")

  ' FAIL LOUDLY if PawnPayments table can't be opened
  On Error Resume Next
  rs.Open "SELECT * FROM [PawnPayments]", conn, adOpenStatic, adLockReadOnly
  If Err.Number <> 0 Then
    WScript.StdErr.WriteLine "FATAL: Cannot open PawnPayments table: " & Err.Description
    outFile.WriteLine "COMMIT;"
    outFile.Close
    WScript.Quit 2
  End If
  On Error Goto 0

  cnt = 0
  Do Until rs.EOF
    sql = "INSERT OR REPLACE INTO PawnPayments (Key,TicketKey,PaymentDate,PawnPmtType,PaymentStatus,TotalDueAmount,NetDueAmount,NetPaymentAmount,Cash,Check_,CreditCard,DebitCard,InterestChargePaid,ServiceChargePaid,PrincipalPaid,NewCurrentBalance,NewDueDate,OldDueDate,OperatorInitials,Method,Note,SyncedAt) VALUES ("

    sql = sql & FormatSqlVal(rs.Fields("Key").Value, rs.Fields("Key").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("TicketKey").Value, rs.Fields("TicketKey").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("PaymentDate").Value, rs.Fields("PaymentDate").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("PawnPmtType").Value, rs.Fields("PawnPmtType").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("PaymentStatus").Value, rs.Fields("PaymentStatus").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("TotalDueAmount").Value, rs.Fields("TotalDueAmount").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("NetDueAmount").Value, rs.Fields("NetDueAmount").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("NetPaymentAmount").Value, rs.Fields("NetPaymentAmount").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("Cash").Value, rs.Fields("Cash").Type)
    ' "Check" is a reserved word — use rs.Fields("Check") to READ from Access (no brackets for ADO field access)
    sql = sql & "," & FormatSqlVal(rs.Fields("Check").Value, rs.Fields("Check").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("CreditCard").Value, rs.Fields("CreditCard").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("DebitCard").Value, rs.Fields("DebitCard").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("InterestChargePaid").Value, rs.Fields("InterestChargePaid").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("ServiceChargePaid").Value, rs.Fields("ServiceChargePaid").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("PrincipalPaid").Value, rs.Fields("PrincipalPaid").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("NewCurrentBalance").Value, rs.Fields("NewCurrentBalance").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("NewDueDate").Value, rs.Fields("NewDueDate").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("OldDueDate").Value, rs.Fields("OldDueDate").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("OperatorInitials").Value, rs.Fields("OperatorInitials").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("Method").Value, rs.Fields("Method").Type)
    sql = sql & "," & FormatSqlVal(rs.Fields("Note").Value, rs.Fields("Note").Type)
    sql = sql & ",'" & Replace(nowIso, "'", "''") & "');"

    outFile.WriteLine sql
    cnt = cnt + 1
    rs.MoveNext
  Loop
  rs.Close

  WScript.StdErr.WriteLine "PawnPayments: exported " & cnt & " rows"
End Sub

Function SqlStr(v)
  If IsNull(v) Or IsEmpty(v) Then SqlStr = "NULL" Else SqlStr = "'" & Replace(Replace(Replace(CStr(v), "'", "''"), vbCrLf, " "), vbCr, " ") & "'"
End Function

Function SqlDate(v)
  If IsNull(v) Or IsEmpty(v) Then SqlDate = "NULL" Else SqlDate = "'" & Replace(FormatISODate(v), "'", "''") & "'"
End Function

Function SqlVal(v)
  If IsNull(v) Or IsEmpty(v) Then SqlVal = "NULL" Else SqlVal = Replace(CStr(v), ",", ".")
End Function

Function FormatSqlVal(v, adoType)
  If IsNull(v) Or IsEmpty(v) Then
    FormatSqlVal = "NULL"
    Exit Function
  End If
  Select Case adoType
    Case 2, 3, 4, 5, 6, 14, 16, 17, 18, 19, 20, 21, 131
      FormatSqlVal = Replace(CStr(v), ",", ".")
    Case 11
      If v Then FormatSqlVal = "-1" Else FormatSqlVal = "0"
    Case 7
      FormatSqlVal = "'" & Replace(FormatISODate(v), "'", "''") & "'"
    Case Else
      FormatSqlVal = "'" & Replace(Replace(Replace(CStr(v), "'", "''"), vbCrLf, " "), vbCr, " ") & "'"
  End Select
End Function

Function FormatISODate(d)
  Dim y, m, dy, h, mn, sc
  y = Year(d) : m = Month(d) : dy = Day(d)
  h = Hour(d) : mn = Minute(d) : sc = Second(d)
  FormatISODate = Right("0000" & y, 4) & "-" & Right("00" & m, 2) & "-" & Right("00" & dy, 2) & "T" & Right("00" & h, 2) & ":" & Right("00" & mn, 2) & ":" & Right("00" & sc, 2)
End Function