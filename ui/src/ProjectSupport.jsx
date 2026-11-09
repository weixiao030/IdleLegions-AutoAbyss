import React, {useState} from 'react';
import {Button, Dialog} from '@radix-ui/themes';
import {ArrowUpRight, Code, DownloadSimple, Heart, X} from '@phosphor-icons/react';
import './support.css';

const repository = 'https://github.com/weixiao030/IdleLegions-AutoAbyss';

export default function ProjectSupport() {
  const [imageError, setImageError] = useState(false);

  return <section className="project-support" aria-labelledby="support-title">
    <div className="project-info">
      <span className="support-mark" aria-hidden="true"><Code size={25}/></span>
      <h2 id="support-title">免费开源，自愿赞赏。</h2>
      <p className="free-notice">该软件免费并开源，如果是付费购买请前往退款</p>
      <div className="source-address">
        <span>开源地址:</span>
        <a href={repository} target="_blank" rel="noopener noreferrer">
          <span>{repository}</span><ArrowUpRight size={17} aria-hidden="true"/>
        </a>
      </div>
      <p className="support-description">喜欢这个小工具，可以自愿支持作者。感谢每一份反馈与支持。</p>
    </div>
    <figure className="appreciation">
      <div className="appreciation-heading"><Heart size={18} aria-hidden="true"/><h3>给作者一点鼓励</h3></div>
      {imageError ? <p role="alert" className="appreciation-error">赞赏码加载失败，请重新打开控制台。</p> :
        <Dialog.Root>
          <Dialog.Trigger>
            <button type="button" className="appreciation-preview" aria-label="放大赞赏码">
              <img src="/appreciation-code.webp" alt="作者提供的微信赞赏码，点击可查看原图" width="600" height="600" onError={() => setImageError(true)}/>
            </button>
          </Dialog.Trigger>
          <Dialog.Content maxWidth="560px" className="appreciation-dialog">
            <div className="appreciation-dialog-heading">
              <Dialog.Title>作者赞赏码</Dialog.Title>
              <Dialog.Close><button type="button" className="dialog-close" aria-label="关闭赞赏码"><X size={20}/></button></Dialog.Close>
            </div>
            <Dialog.Description size="2">自愿支持，所有功能均可免费使用。</Dialog.Description>
            <img className="appreciation-original" src="/appreciation-original.jpg" alt="作者提供的完整微信赞赏码" width="1210" height="1210"/>
            <Dialog.Close><Button variant="soft">关闭</Button></Dialog.Close>
          </Dialog.Content>
        </Dialog.Root>}
      <figcaption>
        <p className="appreciation-note">无论是否打赏都不影响使用，全部免费,感谢板板支持</p>
        <a className="appreciation-download" href="/appreciation-original.jpg" download="IdleLegions赞赏码.jpg"><DownloadSimple size={16} aria-hidden="true"/>保存赞赏码</a>
      </figcaption>
    </figure>
  </section>;
}
