﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadSheetParser
{
    public enum ESheetType
    {
        Class,
        Struct,
        Enum,
        Global,
    }

    public enum EEnumHeaderType
    {
        EnumNone,

        EnumType,
        EnumValue,
        NumberValue,
        Comment,
    }

    public enum ECommandLine
    {
        Error = -1,

        comment,
        ispartial,
        baseis,
        addusing,
        useusing,
    }
    public static class TypeDataHelper
    {
        public const string const_GlobalKey_EnumName = "EGlobalKey";
        public const string const_GlobalKey_FieldName = "eGlobalKey";

        public delegate void delOnParsingText(IList<object> listRow, string strText, int iRowIndex, int iColumnIndex);


        const string const_strCommandString = "#";
        const string const_strIgnoreString_Row = "R";
        const string const_strIgnoreString_Column = "C";
        const string const_strStartString = "Start";

        public static void ParsingSheet(this TypeData pSheet, ISheetConnector pConnector, delOnParsingText OnParsingTextLine)
        {
            IList<IList<Object>> pData = pConnector.ISheetConnector_GetSheetData(pSheet.strSheetName);
            if (pData == null)
                return;

            _ParsingSheet(OnParsingTextLine, pData);
        }

        public static Task ParsingSheet_UseTask(this TypeData pSheet, ISheetConnector pConnector, delOnParsingText OnParsingTextLine)
        {
            return pConnector.ISheetConnector_GetSheetData_Async(pSheet.strSheetName).
                ContinueWith(p => _ParsingSheet(OnParsingTextLine, p.Result));
        }

        public static void DoCheck_IsValid_Table(this TypeData pSheetData, ISheetConnector pConnector, Action<string> OnError)
        {
            bool bIsEnum = pSheetData.eType == ESheetType.Enum;

            pSheetData.ParsingSheet(pConnector,
            (listRow, strText, iRow, iColumn) =>
            {
                if (bIsEnum)
                {
                    Dictionary<int, EEnumHeaderType> mapEnumType = new Dictionary<int, EEnumHeaderType>();

                    if (Enum.TryParse(strText, out EEnumHeaderType eType))
                    {
                        // mapEnumType.Add(,eType)
                        if (eType == EEnumHeaderType.EnumType)
                        {
                            for (int i = iColumn; i < listRow.Count; i++)
                            {
                                string strTextOtherColumn = (string)listRow[i];
                                if (Enum.TryParse(strTextOtherColumn, out eType) == false)
                                {
                                    OnError?.Invoke($"테이블 유효성 체크 - 이넘 파싱 에러");
                                    return;
                                }

                                if (mapEnumType.ContainsKey(iColumn) == false)
                                    mapEnumType.Add(iColumn, eType);
                            }
                        }

                        return;
                    }

                    if (mapEnumType.ContainsKey(iColumn) == false)
                        return;

                    switch (mapEnumType[iColumn])
                    {
                        case EEnumHeaderType.EnumType:
                        case EEnumHeaderType.EnumValue:
                            if (string.IsNullOrEmpty(strText))
                            {
                                OnError?.Invoke($"테이블 유효성 체크 - 이넘 파싱 에러");
                                return;
                            }
                            break;
                    }
                }
                else
                {

                }
            });
        }

        public static Task DoWork(this TypeData pSheetData, ISheetConnector pConnector, CodeFileBuilder pCodeFileBuilder, Action<string> OnError)
        {
            List<CommandLineArg> listCommandLine = Parsing_CommandLine(pSheetData.strCommandLine, OnError);

            switch (pSheetData.eType)
            {
                case ESheetType.Class:
                case ESheetType.Struct:
                    return Parsing_OnCode(pSheetData, pConnector, pCodeFileBuilder, listCommandLine);

                case ESheetType.Enum:
                    return Parsing_OnEnum(pSheetData, pConnector, pCodeFileBuilder);

                case ESheetType.Global:
                    return Parsing_OnGlobal(pSheetData, pConnector, pCodeFileBuilder);

                default: return Task.CompletedTask;
            }
        }

        public enum EGlobalColumnType
        {
            None,

            Key,
            Value,
            Type,
            Comment,

            MAX,
        }

        private static Task Parsing_OnGlobal(TypeData pSheetData, ISheetConnector pConnector, CodeFileBuilder pCodeFileBuilder)
        {
            var pCodeType_Class = pCodeFileBuilder.AddCodeType(pSheetData.strFileName, pSheetData.eType);

            Dictionary<int, EGlobalColumnType> mapGlobalColumnType = new Dictionary<int, EGlobalColumnType>();
            Dictionary<string, CodeTypeDeclaration> mapEnumFieldType = new Dictionary<string, CodeTypeDeclaration>();
            HashSet<string> setGlobalTable_ByType = new HashSet<string>();

            string strKeyFieldName = "";
            string strTypeFieldName = "";
            int iColumnIndex_Type = -1;
            int iColumnIndex_Comment = -1;

            return pSheetData.ParsingSheet_UseTask(pConnector,
              (listRow, strText, iRow, iColumn) =>
              {
                  // 변수 선언 형식인경우
                  if (strText.Contains(":"))
                  {
                      string[] arrText = strText.Split(':');
                      string strFieldName = arrText[0];
                      string strFieldName_Lower = strFieldName.ToLower();

                      for (int i = 0; i < (int)EGlobalColumnType.MAX; i++)
                      {
                          EGlobalColumnType eCurrentColumnType = (EGlobalColumnType)i;
                          if (strFieldName_Lower.Contains(eCurrentColumnType.ToString().ToLower()))
                          {
                              mapGlobalColumnType.Add(iColumn, eCurrentColumnType);

                              switch (eCurrentColumnType)
                              {
                                  case EGlobalColumnType.Key:
                                      {
                                          FieldTypeData pFieldData = pSheetData.listFieldData.FirstOrDefault(p => p.strFieldName == strFieldName);
                                          if (pFieldData != null)
                                              pFieldData.bDeleteThisField_InCode = true;

                                          strKeyFieldName = strFieldName;
                                      }
                                      break;

                                  case EGlobalColumnType.Comment:
                                      {
                                          FieldTypeData pFieldData = pSheetData.listFieldData.FirstOrDefault(p => p.strFieldName == strFieldName);
                                          if (pFieldData != null)
                                              pFieldData.bDeleteThisField_InCode = true;
                                          iColumnIndex_Comment = iColumn;
                                      }
                                      break;

                                  case EGlobalColumnType.Type:
                                      {
                                          iColumnIndex_Type = iColumn;
                                          strTypeFieldName = strFieldName;
                                          pCodeType_Class.AddField(new FieldTypeData(strFieldName, arrText[1]));

                                          FieldTypeData pFieldData_Type = pSheetData.listFieldData.FirstOrDefault(p => p.strFieldName == strFieldName);
                                          if (pFieldData_Type == null)
                                              pSheetData.listFieldData.Add(new FieldTypeData(strFieldName, arrText[1]));
                                      }
                                      break;


                                  default:
                                      pCodeType_Class.AddField(new FieldTypeData(strFieldName, arrText[1]));
                                      break;
                              }

                              return;
                          }
                      }
                  }

                  // 변수 선언이 아니라 값을 파싱하면 일단 타입부터 확인한다
                  if (mapGlobalColumnType.TryGetValue(iColumn, out var eColumnType) == false)
                      return;

                  switch (eColumnType)
                  {
                      case EGlobalColumnType.Key:

                          string strTypeName = (string)listRow[iColumnIndex_Type];
                          string strEnumTypeName = const_GlobalKey_EnumName + "_" + strTypeName;
                          string strEnumFieldName = const_GlobalKey_FieldName + "_" + strTypeName;
                          if (mapEnumFieldType.TryGetValue(strEnumTypeName, out CodeTypeDeclaration pEnumTypeDeclaration) == false)
                          {
                              pEnumTypeDeclaration = AddEnumType(pSheetData, pCodeFileBuilder, strEnumTypeName);
                              mapEnumFieldType.Add(strEnumTypeName, pEnumTypeDeclaration);
                          }


                          FieldTypeData pFieldData_Enum = pSheetData.listFieldData.FirstOrDefault(p => p.strFieldName == strEnumFieldName);
                          if (pFieldData_Enum == null)
                          {
                              pFieldData_Enum = new FieldTypeData(strEnumFieldName, strEnumTypeName);
                              pFieldData_Enum.strDependencyFieldName = strKeyFieldName;
                              pFieldData_Enum.bIsVirtualField = true;
                              pSheetData.listFieldData.Add(pFieldData_Enum);
                          }


                          string strComment = "";
                          if (iColumnIndex_Comment != -1 && iColumnIndex_Comment < listRow.Count)
                              strComment = (string)listRow[iColumnIndex_Comment];

                          if (string.IsNullOrEmpty(strComment))
                              pEnumTypeDeclaration.AddEnumField(new EnumFieldData(strText));
                          else
                              pEnumTypeDeclaration.AddEnumField(new EnumFieldData(strText, strComment));

                          break;

                      case EGlobalColumnType.Type:

                          if (setGlobalTable_ByType.Contains(strText))
                              return;
                          setGlobalTable_ByType.Add(strText);

                          string strFieldType = (string)listRow[iColumnIndex_Type];
                          FieldTypeData pFieldData = pSheetData.listFieldData.FirstOrDefault(p => p.strFieldName == strTypeFieldName && p.strFieldType == strFieldType);
                          if (pFieldData == null)
                          {
                              pFieldData = new FieldTypeData(strTypeFieldName, strFieldType);
                              pSheetData.listFieldData.Add(pFieldData);
                          }

                          pFieldData.bIsTemp = true;
                          pFieldData.bDeleteThisField_InCode = true;
                          pFieldData.bIsVirtualField = strFieldType != "string";
                          pFieldData.bIsKeyField = true;
                          pFieldData.bIsOverlapKey = true;

                          break;
                  }
              });
        }

        private static CodeTypeDeclaration AddEnumType(TypeData pSheetData, CodeFileBuilder pCodeFileBuilder, string strEnumTypeName)
        {
            var pCodeType_GlobalKey = pCodeFileBuilder.AddCodeType(strEnumTypeName, ESheetType.Enum);

            if (pSheetData.listEnumName.Contains(strEnumTypeName) == false)
                pSheetData.listEnumName.Add(strEnumTypeName);

            return pCodeType_GlobalKey;
        }

        private static Task Parsing_OnCode(TypeData pSheetData, ISheetConnector pConnector, CodeFileBuilder pCodeFileBuilder, List<CommandLineArg> listCommandLine)
        {
            var pCodeType = pCodeFileBuilder.AddCodeType(pSheetData.strFileName, pSheetData.eType);
            var mapFieldData_ConvertStringToEnum = pSheetData.listFieldData.Where((pFieldData) => pFieldData.bConvertStringToEnum).ToDictionary(((pFieldData) => pFieldData.strFieldName));
            var listFieldData_DeleteThisField_OnCode = pSheetData.listFieldData.Where((pFieldData) => pFieldData.bDeleteThisField_InCode).Select((pFieldData) => pFieldData.strFieldName);
            Dictionary<int, CodeTypeDeclaration> mapEnumType = new Dictionary<int, CodeTypeDeclaration>();


            int iDefinedTypeRow = -1;

            pSheetData.listEnumName.Clear();
            return pSheetData.ParsingSheet_UseTask(pConnector,
              (listRow, strText, iRow, iColumn) =>
              {
                  // 변수 선언 형식인경우
                  if (strText.Contains(":"))
                  {
                      if (iDefinedTypeRow == -1)
                          iDefinedTypeRow = iRow;

                      if (iDefinedTypeRow != iRow)
                          return;

                      string[] arrText = strText.Split(':');
                      string strFieldName = arrText[0];

                      if (mapFieldData_ConvertStringToEnum.ContainsKey(strFieldName))
                      {
                          FieldTypeData pFieldData = mapFieldData_ConvertStringToEnum[strFieldName];
                          mapEnumType.Add(iColumn, pCodeFileBuilder.AddCodeType(pFieldData.strEnumName, ESheetType.Enum));

                          if (pSheetData.listEnumName.Contains(pFieldData.strEnumName) == false)
                              pSheetData.listEnumName.Add(pFieldData.strEnumName);
                      }

                      // 삭제되는 코드인 경우
                      if (listFieldData_DeleteThisField_OnCode.Contains(strFieldName))
                          return;

                      pCodeType.AddField(new FieldTypeData(strFieldName, arrText[1]));
                      return;
                  }

                  // 이넘 Column인 경우 이넘 생성
                  if (mapEnumType.ContainsKey(iColumn))
                  {
                      mapEnumType[iColumn].AddEnumField(new EnumFieldData(strText));
                      return;
                  }
              }).ContinueWith((p) => Execute_CommandLine(pCodeType, listCommandLine));
        }

        private static Task Parsing_OnEnum(TypeData pSheetData, ISheetConnector pConnector, CodeFileBuilder pCodeFileBuilder)
        {
            Dictionary<int, EEnumHeaderType> mapEnumType = new Dictionary<int, EEnumHeaderType>();
            Dictionary<string, CodeTypeDeclaration> mapEnumValue = new Dictionary<string, CodeTypeDeclaration>();

            return pSheetData.ParsingSheet_UseTask(pConnector,
                (listRow, strText, iRow, iColumn) =>
                {
                    EEnumHeaderType eType = EEnumHeaderType.EnumNone;
                    if (Enum.TryParse(strText, out eType))
                    {
                        if (eType == EEnumHeaderType.EnumType)
                        {
                            if (mapEnumType.ContainsKey(iColumn) == false)
                                mapEnumType.Add(iColumn, eType);

                            for (int i = iColumn; i < listRow.Count; i++)
                            {
                                string strTextOtherColumn = (string)listRow[i];
                                if (Enum.TryParse(strTextOtherColumn, out eType))
                                {
                                    if (mapEnumType.ContainsKey(i) == false)
                                        mapEnumType.Add(i, eType);
                                }
                            }
                        }

                        return;
                    }

                    if (mapEnumType.TryGetValue(iColumn, out eType) == false)
                        return;

                    if (eType != EEnumHeaderType.EnumType)
                        return;

                    if (mapEnumValue.ContainsKey(strText) == false)
                        mapEnumValue.Add(strText, pCodeFileBuilder.AddCodeType(strText, ESheetType.Enum));

                    EnumFieldData pFieldData = new EnumFieldData();
                    for (int i = iColumn; i < listRow.Count; i++)
                    {
                        if (mapEnumType.TryGetValue(i, out eType))
                        {
                            string strNextText = (string)listRow[i];
                            switch (eType)
                            {
                                case EEnumHeaderType.EnumValue:
                                    pFieldData.strValue = strNextText;
                                    break;

                                case EEnumHeaderType.NumberValue: pFieldData.iNumber = int.Parse(strNextText); break;
                                case EEnumHeaderType.Comment: pFieldData.strComment = strNextText; break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(pFieldData.strValue))
                        throw new Exception($"이넘인데 값이 없습니다 - 타입 : {mapEnumValue[strText].Name}");

                    mapEnumValue[strText].AddEnumField(pFieldData);
                });
        }

        public static List<CommandLineArg> Parsing_CommandLine(string strCommandLine, Action<string> OnError)
        {
            return CommandLineParser.Parsing_CommandLine(strCommandLine,
                (string strCommandLineText, out bool bHasValue) =>
                {
                    bool bIsValid = Enum.TryParse(strCommandLineText, out ECommandLine eCommandLine);
                    switch (eCommandLine)
                    {
                        case ECommandLine.comment:
                        case ECommandLine.baseis:
                        case ECommandLine.addusing:
                        case ECommandLine.useusing:
                            bHasValue = true;
                            break;

                        default:
                            bHasValue = false;
                            break;
                    }

                    return bIsValid;
                },

                (string strCommandLineText, CommandLineParser.Error eError) =>
                {
                    OnError?.Invoke($"테이블 유효성 에러 Text : {strCommandLineText} Error : {eError}");
                    // iErrorCount++;
                });
        }


        private static void _ParsingSheet(delOnParsingText OnParsingTextLine, IList<IList<object>> pData)
        {
            if (OnParsingTextLine == null) // For Loop에서 Null Check 방지
                OnParsingTextLine = (a, b, c, d) => { };

            HashSet<int> setIgnoreColumnIndex = new HashSet<int>();
            bool bIsParsingStart = false;

            for (int i = 0; i < pData.Count; i++)
            {
                bool bIsIgnoreRow = false;
                IList<object> listRow = pData[i];
                for (int j = 0; j < listRow.Count; j++)
                {
                    if (setIgnoreColumnIndex.Contains(j))
                        continue;

                    string strText = (string)listRow[j];
                    if (string.IsNullOrEmpty(strText))
                        continue;

                    if (strText.StartsWith(const_strCommandString))
                    {
                        bool bIsContinue = false;
                        if (strText.Contains(const_strIgnoreString_Column))
                        {
                            setIgnoreColumnIndex.Add(j);
                            bIsContinue = true;
                        }

                        if (bIsParsingStart == false)
                        {
                            if (strText.Contains(const_strStartString))
                            {
                                bIsParsingStart = true;
                                bIsContinue = true;
                            }
                        }

                        if (bIsIgnoreRow == false && strText.Contains(const_strIgnoreString_Row))
                        {
                            bIsContinue = true;
                            bIsIgnoreRow = true;
                        }

                        if (bIsContinue)
                            continue;
                    }

                    if (bIsIgnoreRow)
                        continue;

                    if (bIsParsingStart == false)
                        continue;

                    OnParsingTextLine(listRow, strText, i, j);
                }
            }
        }

        private static void Execute_CommandLine(CodeTypeDeclaration pCodeType, List<CommandLineArg> listCommandLine)
        {
            for (int i = 0; i < listCommandLine.Count; i++)
            {
                ECommandLine eCommandLine = (ECommandLine)Enum.Parse(typeof(ECommandLine), listCommandLine[i].strArgName);
                switch (eCommandLine)
                {
                    case ECommandLine.comment:
                        pCodeType.AddComment(listCommandLine[i].strArgValue);
                        break;

                    case ECommandLine.baseis:
                        pCodeType.AddBaseInterface(listCommandLine[i].strArgValue);
                        break;

                    case ECommandLine.ispartial:
                        pCodeType.IsPartial = true;
                        break;

                    default:
                        break;
                }
            }
        }
    }
}
