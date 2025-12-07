# 📘 KerioControlWeb

**KerioControlWeb** — веб-панель на ASP.NET Core для автоматизации работы с [Kerio Control API](https://www.kerio.com/control).  
Позволяет удобно загружать IP, домены, URL, формировать группы и одновременно обновлять Address Groups и URL Groups.

---

## 🚀 Возможности

### 🔐 Авторизация
- Подключение к Kerio Control по IP
- Ввод логина и пароля
- Получение `sessionId` для дальнейших запросов

### 📥 Загрузка данных
- Списки IP-адресов
- Доменные имена (до 6 поддоменов)
- URL (включая hxxp/hxxps)
- IP и домены с портами
- Автоматическая нормализация:  
  - `hxxp://` → `http://`  
  - `hxxps://` → `https://`  
  - `[.]` → `.`

### 📂 Работа с Kerio API
Поддерживаются запросы:
- `IpAddressGroups.get / set`
- `UrlGroups.get / create / set`
- `Batch.run`

### 📋 Удобный интерфейс
- Комбо-боксы для выбора групп
- Многострочное поле для вставки индикаторов
- Копирование, вставка, очистка данных
- Поле для описания группы
- Автоматическое подтверждение изменений

### 🙈 Список исключений
- Исключение определённых IP из загрузки
- Хранение в `exclusions.txt`

### 🛠 Технологии
- ASP.NET Core (C#)
- Kerio Control API (JSON-RPC)
- HttpClient, Newtonsoft.Json
- Поддержка Windows и Linux

---

## 🚀 Быстрый старт

### 1️⃣ Установка и запуск
1. Установите программу https://drive.google.com/file/d/19H3v7fep5ORZqVtX67cuAP6MaF7YDqjB/view?usp=sharing  
2. Запустите `.exe`  
3. Откройте [https://localhost:7135/](https://localhost:7135/)  
4. Код проекта: [GitHub](https://github.com/gamers65/KerioControlWeb)  

> Можно поставить как службу через NSSM, чтобы постоянно не запускать вручную.

### 2️⃣ Python-сервис для распознавания PDF
1. Установите Python: [python.org](https://www.python.org/downloads/windows/)  
   - При установке отметьте: ✔ Add Python to PATH
2. Создайте папку: `C:\PythonIocService\`
3. Скопируйте файл `ioc_service.py`
4. Установите зависимости:  
```powershell
& "C:\Users\<USER>\AppData\Local\Programs\Python\Python310\python.exe" -m pip install fastapi uvicorn PyPDF2 python-multipart
```
### 3️⃣ NSSM — создание службы
1. Имя: IocPythonService
2. Path: C:\Users\<USER>\AppData\Local\Programs\Python\Python310\python.exe
3. Arguments: -m uvicorn ioc_service:app --host 0.0.0.0 --port 8000
4. Startup Dir: C:\PythonIocService
5. Environment:
```powershell
PATH=C:\Users\<USER>\AppData\Local\Programs\Python\Python310;C:\Users\<USER>\AppData\Local\Programs\Python\Python310\Scripts
PYTHONPATH=C:\PythonIocService
```
