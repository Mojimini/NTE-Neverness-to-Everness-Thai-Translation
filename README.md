# NTE ภาษาไทย — Neverness to Everness Thai Translation

ตัวช่วยติดตั้งม็อดภาษาไทยสำหรับเกม **Neverness to Everness (NTE)** บน Windows รองรับเกม **NTE 1.4.5** ใช้ชุดคำแปล **v28c จำนวน 110,260 รายการ** พร้อมคลังฟอนต์และแจ้งเตือนอัปเดตในโปรแกรม

**[ดาวน์โหลดตัวติดตั้ง EXE รุ่นล่าสุด](https://github.com/Mojimini/NTE-Neverness-to-Everness-Thai-Translation/releases/latest)**

ดาวน์โหลดไฟล์ `Thai-Translator-Setup-….exe` จากหน้า Releases แล้วเปิดเพื่อติดตั้ง เลือกตำแหน่งลงโปรแกรมได้ ไม่ต้องดาวน์โหลด ZIP แพ็กเสริมเอง โปรแกรมจะดาวน์โหลดส่วนที่จำเป็นพร้อมตรวจ SHA-256

## เริ่มใช้งาน

1. ติดตั้งโปรแกรมและเลือกโฟลเดอร์เกม NTE

   <img width="450" height="450" alt="image" src="https://github.com/user-attachments/assets/5633f60d-e8ce-40c8-9dba-2caa69e61f0f" />

3. ตั้งภาษาในเกมเป็น **English** และปิดตัวเกมก่อนกด **แปลเกม**

   <img width="1750" height="450" alt="image" src="https://github.com/user-attachments/assets/dc2ce2be-3142-4cfc-9c67-734052444afc" />

4. กดแปลเกมในโปรแกรมให้ขึ้น 100% แล้วเข้าเกมได้เลย
 4.1ตอนกดแปลโปรแกรมจะทำงานประมาณนี้
    -ตรวจความพร้อม — ตรวจตำแหน่งเกม ตัวเกมต้องปิดอยู่ แต่เปิด Launcher ค้างไว้ได้ และป้องกันการทำงานซ้อนกัน
    -เตรียมไฟล์คำแปล — ใช้ชุดไฟล์ที่แปลไว้แล้ว 110,260 รายการ หากไฟล์ขาดหรือเสียจะดาวน์โหลดจาก GitHub และตรวจไฟล์
    -เตรียมฟอนต์ที่เลือก — ฟอนต์เดิมใช้แพ็กสำเร็จรูป หากเลือกฟอนต์อื่น เช่น Monocraft จะสร้างแพ็กให้ตรงกับฟอนต์ไทยและอังกฤษที่เลือก
    -ตรวจไฟล์เกม — ตรวจว่าไฟล์ตรงกับรุ่นที่รองรับ และไม่มีไฟล์ชื่อชนก่อนติดตั้ง
    -เพิ่มไฟล์ลงเกม 6 ไฟล์ — แพ็กคำแปล 4 ไฟล์ และตัวโหลด 2 ไฟล์ พร้อมบันทึกข้อมูลสำหรับคืนค่า โดยเก็บไฟล์ต้นฉบับของเกมไว้
    -ตรวจไฟล์ที่ติดตั้งอีกครั้ง — เมื่อครบและถูกต้องจึงแสดงสถานะสีเขียวและ 100% พร้อมเปิดเกม

   <img width="963" height="791" alt="Animation" src="https://github.com/user-attachments/assets/52ff29a1-9a7f-4461-a467-67b0463efd49" />

5. หลังเล่นเสร็จสามารถกดคืนค่าได้ ไม่จำเป็นต้องคืนค่าทุกครั้งหลังออกเกมคืนค่าแค่ตอนเกมอัwเดทหรือโปรแกรมอัพเดทก็พอ

<img width="901" height="320" alt="image" src="https://github.com/user-attachments/assets/528db514-396a-4bbd-b6d4-55d985ad31ab" />

6. หากปิดโปรแกรมตอนแปลเสร็จแล้วเล่นได้ตามปกติเมื่อเปิดโปรแกรมไหม่โปรแกรมจะตรวจว่าได้ทำการแปลแล้วหรือยัง

<img width="905" height="314" alt="image" src="https://github.com/user-attachments/assets/7a3b3117-63b7-4903-a6bc-0f6f154dd0e5" />

## ฟอนต์และหน้าต่าง

- โหมดมืดเป็นค่าเริ่มต้น สลับมืด/สว่างได้
- เลือกฟอนต์ไทยและอังกฤษแยกกัน พร้อมตัวอย่างข้อความรวม
- นำเข้าไฟล์ `.ttf` เก็บไว้ในคลัง เลือกใช้ภายหลังหรือลบได้ ฟอนต์เริ่มต้นถูกป้องกันไว้
- ฟอนต์อังกฤษ **Monocraft** สไตล์ Minecraft มีให้เลือก
- แพ็กคำแปลและเครื่องมือสร้างฟอนต์ดาวน์โหลดเมื่อจำเป็น ใช้ไฟล์ที่ตรวจแล้วซ้ำได้

## อัปเดตโปรแกรม

เมื่อมีรุ่นใหม่ใน GitHub Releases โปรแกรมจะแสดงแถบแจ้งเตือนพร้อมปุ่ม **อัปเดต** และมีปุ่มตรวจหาอัปเดตในแท็บสถานะ ระบบดาวน์โหลดตัวติดตั้ง ตรวจขนาดและ SHA-256 แล้วติดตั้งกลับตำแหน่งเดิม โดยเก็บค่าตั้ง คลังฟอนต์ และข้อมูลคืนค่าเกมไว้

ถ้าอินเทอร์เน็ตไม่พร้อม โปรแกรมยังเปิดใช้งานไฟล์ที่ดาวน์โหลดแล้วและคืนค่าเกมได้ อัปเดตโปรแกรมไม่ได้ติดตั้งคำแปลชุดใหม่ลงเกมเอง หากแพ็กคำแปลเปลี่ยน ให้คืนค่าเดิมแล้วแปลเกมอีกครั้ง

## ระบบที่รองรับและขอบเขตการตรวจ
รองแค่ PC นะตอนนี้
Windows 10 รุ่น 1903 ขึ้นไป หรือ Windows 11 แบบ 64 บิต พร้อม .NET Framework 4.8 ขึ้นไป ต้องเชื่อมต่ออินเทอร์เน็ตสำหรับดาวน์โหลดส่วนที่ขาดและอัปเดต

คำแปลครบ 110,260 รายการ ตรวจลำดับ รหัส โครงสร้างและ Frozen ตามชุด v28c แล้ว การตรวจข้อความและไฟล์ยังยินยันไม่ได้ 100% ว่าความหมายทุกฉากหรือการแสดงผลของทุกฟอนต์ในเกมครบ รายการที่ต้องตรวจบริบทต่อยังแยกไว้ 2,742 รายการ ผู้ใช้ควรทดลองฟอนต์ที่นำเข้าเองกับสัดส่วนและการตัดบรรทัดในเกม

## แจ้งปัญหา

เปิด [Issue](https://github.com/Mojimini/NTE-Neverness-to-Everness-Thai-Translation/issues) พร้อมรุ่นโปรแกรม รุ่นเกม ขั้นตอนที่ทำและภาพข้อความที่พบ สำหรับปัญหาคำแปล ให้แนบข้อความอังกฤษและบริบทที่พบ หลีกเลี่ยงข้อมูลบัญชีหรือข้อมูลส่วนตัว

## For English readers

**NTE Thai Translator** is a Windows installer for the **Neverness to Everness Thai translation / Thai localization patch**, targeting **NTE 1.4.5**. It offers on-demand verified content downloads, separate Thai and English TTF font libraries, dark/light themes, and GitHub Releases update notifications. Set the game's language to English and close the game before applying the patch; its Launcher may stay open. Download the setup EXE from [Releases](https://github.com/Mojimini/NTE-Neverness-to-Everness-Thai-Translation/releases/latest).

Developer build and publishing instructions: [BUILD.md](BUILD.md). Third-party notices: [THIRD_PARTY.md](THIRD_PARTY.md).
