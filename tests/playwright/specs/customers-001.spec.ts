import { test, expect } from '@playwright/test';
import { spawn, type ChildProcess } from 'node:child_process';
import path from 'node:path';
import { readFileSync } from 'node:fs';

// CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — real MVC controllers/Blazor components hosted only against an in-memory service on loopback.
const root=path.resolve(__dirname,'../../..');const base='http://127.0.0.1:5201';let server:ChildProcess;
test.describe('@customers-001 isolated functional acceptance',()=>{
 test.describe.configure({mode:'serial'});
 test.beforeAll(async()=>{
  const cwd=path.join(root,'tests/TAPortal.CustomerHarness');
  server=spawn('dotnet',[path.join(cwd,'bin/Release/net8.0/TAPortal.CustomerHarness.dll')],{cwd,windowsHide:true,stdio:'ignore',env:{...process.env,TAPORTAL_CUSTOMERS_HARNESS:'1',ASPNETCORE_ENVIRONMENT:'Development'}});
  for(let i=0;i<60;i++){try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,250));}throw new Error('Isolated harness failed startup');
 });
 test.afterAll(async()=>{if(server?.pid)await new Promise<void>(resolve=>spawn('taskkill',['/PID',String(server.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'}).once('exit',()=>resolve()));});
 for(const route of ['/customers','/blazor-test']){
  for(const width of [1440,768,390]){
   test(`${route} create, validate, search and download at ${width}`,async({page,request},info)=>{
    test.setTimeout(60000);await request.get(base+'/test/reset');await page.setViewportSize({width,height:width===1440?900:width===768?1024:844});
    const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto(base+route,{waitUntil:'networkidle'});
    await expect(page.locator('.ta-customer-empty')).toContainText('Chưa có khách hàng.');
    await page.getByRole('button',{name:'Thêm khách hàng',exact:true}).click();
    await expect(page.locator('dialog')).toBeVisible();
    await page.getByRole('button',{name:'Lưu khách hàng',exact:true}).click();
    await expect(page.locator('dialog')).toBeVisible();
    await page.getByLabel('Mã khách hàng',{exact:true}).fill('C001');await page.getByLabel('Tên khách hàng',{exact:true}).fill('Customer One');
    await page.getByLabel('Email',{exact:true}).fill('bad-email');await page.getByRole('button',{name:'Lưu khách hàng',exact:true}).click();await expect(page.locator('dialog')).toBeVisible();
    await page.getByLabel('Email',{exact:true}).fill('customer@example.com');await page.getByLabel('Điện thoại',{exact:true}).fill('0123456789');
    await page.screenshot({path:info.outputPath(`modal-${width}.png`)});
    await page.getByRole('button',{name:'Lưu khách hàng',exact:true}).click();
    await expect(page.locator('dialog')).not.toBeVisible();await expect(page.locator('tbody')).toContainText('Customer One');
    await page.getByRole('button',{name:'Thêm khách hàng',exact:true}).click();await page.getByLabel('Mã khách hàng',{exact:true}).fill('C001');await page.getByLabel('Tên khách hàng',{exact:true}).fill('Duplicate');
    await page.getByRole('button',{name:'Lưu khách hàng',exact:true}).click();await expect(page.locator('dialog')).toContainText('Mã khách hàng đã tồn tại.');
    await page.getByRole('button',{name:'Hủy',exact:true}).click();await expect(page.locator('dialog')).not.toBeVisible();
    await page.getByRole('searchbox').fill('missing');await expect(page.locator('.ta-customer-empty')).toContainText('Không tìm thấy');
    await page.getByRole('searchbox').fill('C001');
    const download=page.waitForEvent('download');await page.getByRole('button',{name:'Xuất dữ liệu',exact:true}).click();const file=await download;
    expect(file.suggestedFilename()).toMatch(/^Khach-hang-\d{8}\.xlsx$/);const filePath=info.outputPath(`customers-${width}.xlsx`);await file.saveAs(filePath);expect(readFileSync(filePath).subarray(0,2).toString()).toBe('PK');
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);expect(errors).toEqual([]);
    await page.screenshot({path:info.outputPath(`customers-${width}.png`)});
   });
  }
 }
 test('MVC rejects revoked create and missing anti-forgery token',async({page,request})=>{
  await request.get(base+'/test/reset');await page.goto(base+'/customers');await page.getByRole('button',{name:'Thêm khách hàng',exact:true}).click();
  await page.getByLabel('Mã khách hàng',{exact:true}).fill('DENIED');await page.getByLabel('Tên khách hàng',{exact:true}).fill('Denied');await request.get(base+'/test/deny');
  const response=page.waitForResponse(r=>r.url().toLowerCase().endsWith('/customers/create'));await page.getByRole('button',{name:'Lưu khách hàng',exact:true}).click();expect((await response).status()).toBe(403);await expect(page.locator('dialog')).toBeVisible();
  await request.get(base+'/test/reset');expect((await request.post(base+'/customers/create',{form:{Code:'NO-CSRF',Name:'Invalid'}})).status()).toBe(400);
 });
});
