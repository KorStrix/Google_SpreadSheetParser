﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;

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
    static public class TypeDataHelper
    {
        public const string const_GlobalKey_EnumName = "EGlobalKey";
        public const string const_GlobalKey_FieldName = "eGlobalKey";

        public delegate void delOnParsingText(IList<object> listRow, string strText, int iRowIndex, int iColumnIndex);

        static public void ParsingSheet(this TypeData pSheet, SpreadSheetConnector pConnector, delOnParsingText OnParsingText)
        {
            const string const_strCommandString = "#";
            const string const_strIgnoreString_Row = "R";
            const string const_strIgnoreString_Column = "C";
            const string const_strStartString = "Start";

            if (pConnector == null)
                return;

            IList<IList<Object>> pData = pConnector.GetExcelData(pSheet.strSheetName);
            if (pData == null)
                return;

            if (OnParsingText == null) // For Loop에서 Null Check 방지
                OnParsingText = (a, b, c, d) => { };

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

                    OnParsingText(listRow, strText, i, j);
                }
            }
        }

        static public void DoCheck_IsValid_Table(this TypeData pSheetData, SpreadSheetConnector pConnector, System.Action<string> OnError)
        {
            bool bIsEnum = pSheetData.eType == ESheetType.Enum;

            pSheetData.ParsingSheet(pConnector,
            (listRow, strText, iRow, iColumn) =>
            {
                if (bIsEnum)
                {
                    Dictionary<int, EEnumHeaderType> mapEnumType = new Dictionary<int, EEnumHeaderType>();

                    EEnumHeaderType eType = EEnumHeaderType.EnumNone;
                    if (System.Enum.TryParse(strText, out eType))
                    {
                        // mapEnumType.Add(,eType)
                        if (eType == EEnumHeaderType.EnumType)
                        {
                            for (int i = iColumn; i < listRow.Count; i++)
                            {
                                string strTextOtherColumn = (string)listRow[i];
                                if (System.Enum.TryParse(strTextOtherColumn, out eType) == false)
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

        static public void DoWork(this TypeData pSheetData, SpreadSheetConnector pConnector, CodeFileBuilder pCodeFileBuilder, System.Action<string> OnError)
        {
            List<CommandLineArg> listCommandLine = Parsing_CommandLine(pSheetData.strCommandLine, OnError);

            switch (pSheetData.eType)
            {
                case ESheetType.Class:
                case ESheetType.Struct:
                    Parsing_OnCode(pSheetData, pConnector, pCodeFileBuilder, listCommandLine);
                    break;

                case ESheetType.Enum:
                    Parsing_OnEnum(pSheetData, pConnector, pCodeFileBuilder);
                    break;

                case ESheetType.Global:
                    Parsing_OnGlobal(pSheetData, pConnector, pCodeFileBuilder);
                    break;
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

        private static void Parsing_OnGlobal(TypeData pSheetData, SpreadSheetConnector pConnector, CodeFileBuilder pCodeFileBuilder)
        {
            // 글로벌 테이블은 데이터를 담는 데이터컨테이너와 글로벌키 Enum타입 2개를 생성한다.
            var pCodeType_Class = pCodeFileBuilder.AddCodeType(pSheetData.strFileName, pSheetData.eType);
            var pCodeType_GlobalKey = pCodeFileBuilder.AddCodeType(const_GlobalKey_EnumName, ESheetType.Enum);

            Dictionary<int, EGlobalColumnType> mapGlobalColumnType = new Dictionary<int, EGlobalColumnType>();
            HashSet<string> setGlobalTable_ByType = new HashSet<string>();

            string strTypeName = "";
            int iColumnIndex_Type = -1;
            int iColumnIndex_Comment = -1;

            pSheetData.ParsingSheet(pConnector,
              (listRow, strText, iRow, iColumn) =>
              {
                  // 변수 선언 형식인경우
                  if (strText.Contains(":"))
                  {
                      string[] arrText = strText.Split(':');
                      string strFieldName = arrText[0];
                      string strFieldName_Lower = strFieldName.ToLower();

                      for(int i = 0; i < (int)EGlobalColumnType.MAX; i++)
                      {
                          EGlobalColumnType eCurrentColumnType = (EGlobalColumnType)i;
                          if (strFieldName_Lower.Contains(eCurrentColumnType.ToString().ToLower()))
                          {
                              mapGlobalColumnType.Add(iColumn, eCurrentColumnType);

                              switch (eCurrentColumnType)
                              {
                                  case EGlobalColumnType.Key:
                                      {
                                          FieldTypeData pFieldData = pSheetData.listFieldData.Where(p => p.strFieldName == strFieldName).FirstOrDefault();
                                          if (pFieldData != null)
                                              pFieldData.bDeleteThisField_InCode = true;

                                          FieldTypeData pFieldData_Enum = pSheetData.listFieldData.Where(p => p.strFieldName == const_GlobalKey_FieldName).FirstOrDefault();
                                          if(pFieldData_Enum == null)
                                          {
                                              pFieldData_Enum = new FieldTypeData(const_GlobalKey_FieldName, const_GlobalKey_EnumName);
                                              pFieldData_Enum.strDependencyFieldName = pFieldData.strFieldName;
                                              pFieldData_Enum.bIsVirtualField = true;
                                              pSheetData.listFieldData.Add(pFieldData_Enum);
                                          }
                                      }

                                      break;

                                  case EGlobalColumnType.Comment:
                                      {
                                          FieldTypeData pFieldData = pSheetData.listFieldData.Where(p => p.strFieldName == strFieldName).FirstOrDefault();
                                          if (pFieldData != null)
                                              pFieldData.bDeleteThisField_InCode = true;
                                          iColumnIndex_Comment = iColumn;
                                      }
                                      break;

                                  case EGlobalColumnType.Type:
                                      iColumnIndex_Type = iColumn;
                                      strTypeName = strFieldName;
                                      pCodeType_Class.AddField(new FieldTypeData(strFieldName, arrText[1]));

                                      FieldTypeData pFieldData_Type = pSheetData.listFieldData.Where(p => p.strFieldName == strFieldName).FirstOrDefault();
                                      if (pFieldData_Type == null)
                                          pSheetData.listFieldData.Add(new FieldTypeData(strFieldName, arrText[1]));

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
                  EGlobalColumnType eColumnType = EGlobalColumnType.None;
                  if (mapGlobalColumnType.TryGetValue(iColumn, out eColumnType) == false)
                      return;

                  switch (eColumnType)
                  {
                      case EGlobalColumnType.Key:

                          string strComment = "";
                          if (iColumnIndex_Comment != -1 && iColumnIndex_Comment < listRow.Count)
                              strComment = (string)listRow[iColumnIndex_Comment];

                          if (string.IsNullOrEmpty(strComment))
                              pCodeType_GlobalKey.AddEnumField(new EnumFieldData(strText));
                          else
                              pCodeType_GlobalKey.AddEnumField(new EnumFieldData(strText, strComment));

                          break;

                      case EGlobalColumnType.Type:

                          if (setGlobalTable_ByType.Contains(strText))
                              return;
                          setGlobalTable_ByType.Add(strText);

                          string strFieldType = (string)listRow[iColumnIndex_Type];
                          FieldTypeData pFieldData = pSheetData.listFieldData.Where(p => p.strFieldName == strTypeName && p.strFieldType == strFieldType).FirstOrDefault();
                          if (pFieldData == null)
                          {
                              pFieldData = new FieldTypeData(strTypeName, strFieldType);
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

        private static void Parsing_OnCode(TypeData pSheetData, SpreadSheetConnector pConnector, CodeFileBuilder pCodeFileBuilder, List<CommandLineArg> listCommandLine)
        {
            var pCodeType = pCodeFileBuilder.AddCodeType(pSheetData.strFileName, pSheetData.eType);
            var mapFieldData_ConvertStringToEnum = pSheetData.listFieldData.Where((pFieldData) => pFieldData.bConvertStringToEnum).ToDictionary(((pFieldData) => pFieldData.strFieldName));
            var listFieldData_DeleteThisField_OnCode = pSheetData.listFieldData.Where((pFieldData) => pFieldData.bDeleteThisField_InCode).Select((pFieldData) => pFieldData.strFieldName);
            Dictionary<int, CodeTypeDeclaration> mapEnumType = new Dictionary<int, CodeTypeDeclaration>();


            int iDefinedTypeRow = -1;

            pSheetData.ParsingSheet(pConnector, 
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
              });

            Execute_CommandLine(pCodeType, listCommandLine);
        }

        private static void Parsing_OnEnum(TypeData pSheetData, SpreadSheetConnector pConnector, CodeFileBuilder pCodeFileBuilder)
        {
            Dictionary<int, EEnumHeaderType> mapEnumType = new Dictionary<int, EEnumHeaderType>();
            Dictionary<string, CodeTypeDeclaration> mapEnumValue = new Dictionary<string, CodeTypeDeclaration>();

            pSheetData.ParsingSheet(pConnector,
                (listRow, strText, iRow, iColumn) =>
                {
                    EEnumHeaderType eType = EEnumHeaderType.EnumNone;
                    if (System.Enum.TryParse(strText, out eType))
                    {
                        if (eType == EEnumHeaderType.EnumType)
                        {
                            if (mapEnumType.ContainsKey(iColumn) == false)
                                mapEnumType.Add(iColumn, eType);

                            for (int i = iColumn; i < listRow.Count; i++)
                            {
                                string strTextOtherColumn = (string)listRow[i];
                                if (System.Enum.TryParse(strTextOtherColumn, out eType))
                                {
                                    if (mapEnumType.ContainsKey(i) == false)
                                        mapEnumType.Add(i, eType);
                                }
                            }
                        }

                        return;
                    }

                    eType = mapEnumType[iColumn];
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
                        throw new System.Exception($"이넘인데 값이 없습니다 - 타입 : {mapEnumValue[strText].Name}");

                    mapEnumValue[strText].AddEnumField(pFieldData);
                });
        }

        public static List<CommandLineArg> Parsing_CommandLine(string strCommandLine, System.Action<string> OnError)
        {
            return CommandLineParser.Parsing_CommandLine(strCommandLine,
                (string strCommandLineText, out bool bHasValue) =>
                {
                    ECommandLine eCommandLine;
                    bool bIsValid = Enum.TryParse(strCommandLineText, out eCommandLine);
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

        static private void Execute_CommandLine(CodeTypeDeclaration pCodeType, List<CommandLineArg> listCommandLine)
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
