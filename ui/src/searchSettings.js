export const searchDefaults={mode:'manual',iterations:1024,adaptiveIterations:4096,neuralIterations:2048,horizon:20,workers:4,exploration:1.4,autoTrain:true,trainingEpochs:12,trainingDevice:'cpu'};
export function searchError(value) {
 const v={...searchDefaults,...value};
 if(value!==undefined&&(value===null||typeof value!=='object'||Array.isArray(value))||typeof v.autoTrain!=='boolean')return '搜索配置无效。';
 for(const [key,min,max] of [['iterations',1,1000000],['adaptiveIterations',1,1000000],['neuralIterations',1,1000000],['horizon',1,128],['workers',1,64],['trainingEpochs',1,500]])
  if(!Number.isInteger(v[key])||v[key]<min||v[key]>max)return '搜索次数、步长、并发和训练轮数须在标注范围内填写整数。';
 if(typeof v.exploration!=='number'||!Number.isFinite(v.exploration)||v.exploration<=0)return '探索系数须为正数。';
 if(!['auto','manual'].includes(v.mode))return '参数配置方式须选择 Auto 或手动。';
 if(!['cpu','cuda'].includes(v.trainingDevice))return '训练设备须选择 CPU 或 CUDA。';
 return '';
}
