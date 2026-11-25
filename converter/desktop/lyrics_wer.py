import re,sys
def words(t):
    t=t.lower().replace("’","'")
    t=re.sub(r"\[[^\]]*\]"," ",t)
    t=re.sub(r"in'(?=[\s,.!?)]|$)","ing",t)
    t=re.sub(r"\blil'?(?=\s|$)","little",t)
    t=re.sub(r"'(?=[a-z])","",t)
    return [w for w in re.sub(r"[^a-z0-9' ]"," ",t).split() if w.strip("'")]
def lrc_text(p):
    out=[]
    for l in open(p,encoding='utf-8'):
        out.append(re.sub(r"^(\[[^\]]*\])+","",l.strip()))
    return " ".join(out)
def wer(ref,hyp):
    r,h=words(ref),words(hyp)
    d=list(range(len(h)+1))
    for i in range(1,len(r)+1):
        prev=d[:]; d[0]=i
        for j in range(1,len(h)+1):
            d[j]=min(prev[j]+1,d[j-1]+1,prev[j-1]+(r[i-1]!=h[j-1]))
    return d[len(h)]/max(1,len(r)), len(r), len(h)
if __name__=="__main__":
    ref=lrc_text(sys.argv[1]); hyp=open(sys.argv[2],encoding='utf-8').read()
    if sys.argv[2].endswith('.lrc'): hyp=lrc_text(sys.argv[2])
    e,n,m=wer(ref,hyp); print("%s: word error rate %.0f%% (%d words in the real lyrics, %d heard)"%(sys.argv[3] if len(sys.argv)>3 else '',100*e,n,m))
